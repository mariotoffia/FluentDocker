using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models.Inference;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Server-Sent-Events (SSE) streaming for the inference driver. DMR streams
  /// chat/completions as lines prefixed <c>data: </c>, terminated by
  /// <c>data: [DONE]</c>. Read line-by-line (no buffering); honor cancellation per
  /// read; mid-stream parse faults throw <see cref="ModelRunnerException"/>.
  /// </summary>
  public partial class DockerApiModelInferenceDriver
  {
    /// <summary>
    /// Maximum size (in characters, an upper bound on UTF-8 bytes since every char is at least
    /// one byte) that a single SSE line/event may reach before the stream is aborted. Generous
    /// enough for legitimately large completion frames (1 MiB) yet bounded so a runaway or
    /// hostile server cannot force unbounded buffering. Exceeding it throws a typed
    /// <see cref="ModelRunnerException"/> rather than silently growing memory.
    /// </summary>
    private const int MaxSseLineBytes = 1024 * 1024;

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatCompletionChunk> ChatCompletionStreamAsync(
        DriverContext context, ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      // Copy so we never mutate the caller's instance (Stream is forced on here).
      var req = new ChatCompletionRequest(request) { Stream = true };
      await foreach (var chunk in StreamAsync<ChatCompletionChunk>(
          "/chat/completions", req, cancellationToken).ConfigureAwait(false))
        yield return chunk;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<CompletionChunk> CompletionStreamAsync(
        DriverContext context, CompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      // Copy so we never mutate the caller's instance (Stream is forced on here).
      var req = new CompletionRequest(request) { Stream = true };
      await foreach (var chunk in StreamAsync<CompletionChunk>(
          "/completions", req, cancellationToken).ConfigureAwait(false))
        yield return chunk;
    }

    private async IAsyncEnumerable<T> StreamAsync<T>(string suffix, object request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      var json = JsonSerializer.Serialize(request, JsonHelper.DefaultOptions);
      using var content = new StringContent(json, Encoding.UTF8, "application/json");

      // Open the stream outside the read loop so a failure to open (e.g. 404 for a
      // not-loaded model, 401 for a bad key) maps to the SAME typed ModelRunnerException
      // as the non-streaming PostJsonAsync path — not a raw HttpRequestException.
      Stream stream;
      try
      {
        stream = await _connection.PostStreamAsync(Path(suffix), content, cancellationToken).ConfigureAwait(false);
      }
      catch (HttpRequestException ex)
      {
        throw new ModelRunnerException(ex.Message, ErrorCodeFor(ex.StatusCode), ex);
      }

      await using var owned = stream.ConfigureAwait(false);
      using var reader = new StreamReader(stream, Encoding.UTF8);

      while (true)
      {
        string line;
        try
        {
          line = await ReadBoundedLineAsync(reader, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
          // Transport faults mid-stream (connection reset, socket error) must map to the
          // same typed ModelRunnerException as the open path — never escape as a raw fault.
          throw new ModelRunnerException(
              "Inference stream transport failure", ErrorCodes.ModelInference.EndpointUnreachable, ex);
        }

        if (line == null)
          yield break;

        if (line.Length == 0 || !line.StartsWith("data:", StringComparison.Ordinal))
          continue;

        var payload = line.AsSpan(5).Trim();

        // Blank `data:` frames (and SSE keep-alive/comment lines, already filtered above) are
        // normal heartbeats — skip them BEFORE the [DONE] check / deserialize so an empty
        // payload never reaches the parser and aborts the whole stream.
        if (payload.IsEmpty)
          continue;

        if (payload.SequenceEqual("[DONE]"))
          yield break;

        // A mid-stream error frame (e.g. {"error":{"message":"context length exceeded"}})
        // deserializes into a non-null chunk with no choices, so the plain T-deserialize below
        // would silently yield an empty chunk and lose the error. Detect it first and surface
        // the server's message as a typed failure.
        if (TryGetSseError(payload, out var errorMessage))
          throw new ModelRunnerException(errorMessage, ErrorCodes.ModelInference.RequestFailed);

        T chunk;
        try
        {
          chunk = JsonSerializer.Deserialize<T>(payload, JsonHelper.CaseInsensitiveOptions);
        }
        catch (JsonException ex)
        {
          throw new ModelRunnerException("Malformed SSE chunk", ErrorCodes.ModelInference.StreamParseError, ex);
        }

        // STJ deserializes a literal `data: null` frame to default(T) without throwing;
        // emitting it would surface as a downstream NullReferenceException.
        if (chunk == null)
          throw new ModelRunnerException("Null SSE chunk", ErrorCodes.ModelInference.StreamParseError);

        yield return chunk;
      }
    }

    // Reads a single line, enforcing MaxSseLineBytes so a runaway/hostile server cannot force
    // unbounded buffering (StreamReader.ReadLineAsync has no such cap). Returns null at EOF.
    private static async Task<string> ReadBoundedLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
      var builder = new StringBuilder();
      var buffer = new char[1];

      while (true)
      {
        cancellationToken.ThrowIfCancellationRequested();
        var read = await reader.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
        if (read == 0)
          return builder.Length == 0 ? null : builder.ToString();

        var c = buffer[0];
        if (c == '\n')
          return builder.ToString();
        if (c == '\r')
          continue; // CR is part of CRLF terminators; LF ends the line.

        if (builder.Length >= MaxSseLineBytes)
          throw new ModelRunnerException(
              $"SSE line exceeded the {MaxSseLineBytes}-byte limit", ErrorCodes.ModelInference.StreamParseError);

        builder.Append(c);
      }
    }

    // Parses an SSE payload and reports whether it is an OpenAI-style error envelope
    // ({"error":{"message":"..."}} or {"error":"..."}), extracting the human-readable message.
    private static bool TryGetSseError(ReadOnlySpan<char> payload, out string message)
    {
      message = null;

      // Cheap pre-check: only objects whose top level mentions "error" can be an error envelope,
      // so a normal content chunk avoids the JsonDocument allocation/parse entirely.
      if (payload.IndexOf("\"error\"", StringComparison.Ordinal) < 0)
        return false;

      try
      {
        using var doc = JsonDocument.Parse(payload.ToString().AsMemory());
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("error", out var error))
          return false;

        message = error.ValueKind switch
        {
          JsonValueKind.Object when error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
              => m.GetString(),
          JsonValueKind.String => error.GetString(),
          _ => null
        };
        message ??= "Inference stream returned an error frame";
        return true;
      }
      catch (JsonException)
      {
        // Not parseable here; let the normal T-deserialize path raise StreamParseError.
        return false;
      }
    }
  }
}
