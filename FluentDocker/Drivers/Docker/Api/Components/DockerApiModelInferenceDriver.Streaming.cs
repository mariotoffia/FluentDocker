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
        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line == null)
          yield break;

        if (line.Length == 0 || !line.StartsWith("data:", StringComparison.Ordinal))
          continue;

        var payload = line.AsSpan(5).Trim();
        if (payload.SequenceEqual("[DONE]"))
          yield break;

        T chunk;
        try
        {
          chunk = JsonSerializer.Deserialize<T>(payload, JsonHelper.CaseInsensitiveOptions);
        }
        catch (JsonException ex)
        {
          throw new ModelRunnerException("Malformed SSE chunk", ErrorCodes.ModelInference.StreamParseError, ex);
        }

        yield return chunk;
      }
    }
  }
}
