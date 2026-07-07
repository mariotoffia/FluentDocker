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

namespace FluentDocker.Drivers.Models
{
  /// <summary>
  /// Server-Sent-Events (SSE) streaming for the inference driver. Reads the response as a proper
  /// SSE byte stream: chunked reads decode UTF-8 into lines, consecutive <c>data:</c> lines
  /// accumulate into ONE event dispatched at the blank-line delimiter (per the SSE spec), and
  /// <c>data: [DONE]</c> terminates. A single idle timeout bounds each read (never per character);
  /// caller cancellation surfaces as <see cref="OperationCanceledException"/> while an idle/read
  /// timeout maps to <see cref="ErrorCodes.ModelInference.Timeout"/> and mid-stream transport
  /// faults map to <see cref="ErrorCodes.ModelInference.EndpointUnreachable"/>.
  /// </summary>
  public partial class OpenAiModelInferenceDriver
  {
    /// <summary>
    /// Maximum number of characters a single SSE line may reach before the stream is aborted.
    /// Bounds how much one line buffers (1M chars) so a runaway or hostile server cannot force
    /// unbounded buffering; exceeding it throws a typed <see cref="ModelRunnerException"/>.
    /// </summary>
    private const int MaxSseLineChars = 1024 * 1024;

    /// <summary>
    /// Maximum characters accumulated for one SSE event before the stream is aborted.
    /// Bounds many small <c>data:</c> lines without a blank delimiter.
    /// </summary>
    private const int MaxSseEventChars = 1024 * 1024;

    /// <summary>
    /// Chunk size (bytes/chars) for the streaming reader's reused buffers.
    /// ponytail: 4096 matches <see cref="StreamReader"/>'s default and comfortably holds a DMR SSE
    /// frame in one read; bump it only if profiling shows read-syscall overhead on very
    /// high-throughput streams (it only affects buffering granularity, never correctness).
    /// </summary>
    private const int StreamBufferBytes = 4096;

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatCompletionChunk> ChatCompletionStreamAsync(
        DriverContext context, ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      // Copy so we never mutate the caller's instance (Stream is forced on here).
      var req = new ChatCompletionRequest(request) { Stream = true };
      await foreach (var chunk in StreamAsync<ChatCompletionChunk>(
          context, "/chat/completions", req, "ChatCompletionStream", cancellationToken).ConfigureAwait(false))
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
          context, "/completions", req, "CompletionStream", cancellationToken).ConfigureAwait(false))
        yield return chunk;
    }

    private async IAsyncEnumerable<T> StreamAsync<T>(
        DriverContext context, string suffix, object request, string operation,
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
        var message = ex.StatusCode.HasValue
            ? FormatHttpError(operation, suffix, request, ex.StatusCode.Value, ex.Message)
            : ex.Message;
        throw new ModelRunnerException(
            message, ErrorCodeFor(ex.StatusCode, ex.Message),
            CreateStreamErrorContext(context, operation, ex.StatusCode), ex);
      }
      catch (ModelRunnerException ex) when (ex.Context is null)
      {
        throw new ModelRunnerException(
            ex.Message, ex.ErrorCode, CreateStreamErrorContext(context, operation), ex);
      }

      await using var owned = stream.ConfigureAwait(false);
      var firstByteTimeout = _connection.StreamFirstByteTimeout;
      var idleTimeout = _connection.StreamReadIdleTimeout;

      // Manually enumerate the SSE events so a transport fault raised mid-read maps to the same
      // typed EndpointUnreachable as the open path (a `yield return` cannot live inside a try/catch).
      // try/finally (not `await using`) so the enumerator disposal keeps ConfigureAwait(false).
      var events = ReadSseEventsAsync(
          stream, firstByteTimeout, idleTimeout, context, operation, cancellationToken)
          .GetAsyncEnumerator(cancellationToken);
      try
      {
        while (true)
        {
          string payload;
          try
          {
            if (!await events.MoveNextAsync().ConfigureAwait(false))
              yield break;
            payload = events.Current;
          }
          catch (Exception ex) when (ex is IOException or HttpRequestException)
          {
            throw new ModelRunnerException(
                "Inference stream transport failure", ErrorCodes.ModelInference.EndpointUnreachable,
                CreateStreamErrorContext(context, operation), ex);
          }

          var span = payload.AsSpan().Trim();

          // Blank/heartbeat events carry no payload — skip before [DONE]/deserialize so an empty
          // frame never reaches the parser and aborts the whole stream.
          if (span.IsEmpty)
            continue;

          if (span.SequenceEqual("[DONE]"))
            yield break;

          // A mid-stream error frame (e.g. {"error":{"message":"context length exceeded"}})
          // deserializes into a non-null chunk with no choices, silently losing the error — detect
          // it first and surface the server's message as a typed failure.
          if (TryGetSseError(span, out var errorMessage))
            throw new ModelRunnerException(
                errorMessage, ErrorCodes.ModelInference.RequestFailed,
                CreateStreamErrorContext(context, operation));

          T chunk;
          try
          {
            chunk = JsonSerializer.Deserialize<T>(span, JsonHelper.CaseInsensitiveOptions);
          }
          catch (JsonException ex)
          {
            throw new ModelRunnerException(
                "Malformed SSE chunk", ErrorCodes.ModelInference.StreamParseError,
                CreateStreamErrorContext(context, operation), ex);
          }

          // STJ deserializes a literal `data: null` frame to default(T) without throwing;
          // emitting it would surface as a downstream NullReferenceException.
          if (chunk == null)
            throw new ModelRunnerException(
                "Null SSE chunk", ErrorCodes.ModelInference.StreamParseError,
                CreateStreamErrorContext(context, operation));

          yield return chunk;
        }
      }
      finally
      {
        await events.DisposeAsync().ConfigureAwait(false);
      }
    }

    // Frames SSE events: accumulates consecutive `data:` lines (stripping `data:` + one optional
    // leading space per the spec) and dispatches the JOINED payload (LF-joined) as ONE event at the
    // blank-line delimiter. `:` comment lines and other SSE fields are ignored. A trailing event
    // with no closing blank line is flushed at EOF (lenient, like real SSE clients).
    private static async IAsyncEnumerable<string> ReadSseEventsAsync(
        Stream stream, TimeSpan? firstByteTimeout, TimeSpan? idleTimeout,
        DriverContext context, string operation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      var data = new List<string>();
      var eventChars = 0;
      var firstLine = true;

      await foreach (var rawLine in ReadLinesAsync(
          stream, firstByteTimeout, idleTimeout, context, operation, cancellationToken).ConfigureAwait(false))
      {
        var line = rawLine;
        if (firstLine)
        {
          firstLine = false;
          if (line.Length > 0 && line[0] == '\uFEFF')
            line = line.Substring(1);
        }

        if (line.Length == 0)
        {
          // Event delimiter: dispatch the accumulated data lines as one event (only if any were
          // seen, so a stray blank line does not emit a spurious empty event).
          if (data.Count > 0)
          {
            yield return string.Join("\n", data);
            data.Clear();
            eventChars = 0;
          }
          continue;
        }

        if (line[0] == ':')
          continue; // SSE comment / keep-alive.

        if (line.StartsWith("data:", StringComparison.Ordinal))
        {
          var value = line.Substring(5);
          if (value.Length > 0 && value[0] == ' ')
            value = value.Substring(1); // strip exactly ONE optional leading space.
          eventChars += value.Length + (data.Count > 0 ? 1 : 0);
          if (eventChars > MaxSseEventChars)
            throw new ModelRunnerException(
                $"SSE event exceeded the {MaxSseEventChars}-character limit",
                ErrorCodes.ModelInference.StreamParseError, CreateStreamErrorContext(context, operation));
          data.Add(value);
        }
        // event:/id:/retry: fields are irrelevant to chunk streaming — ignore them.
      }

      if (data.Count > 0)
        yield return string.Join("\n", data);
    }

    // Splits the byte stream into lines, decoding UTF-8 in reused chunks (MR5: ONE idle timeout per
    // read, not per character). Handles LF, CRLF and lone-CR terminators across chunk boundaries,
    // enforces MaxSseLineChars per line, and flushes a final unterminated line at EOF.
    private static async IAsyncEnumerable<string> ReadLinesAsync(
        Stream stream, TimeSpan? firstByteTimeout, TimeSpan? idleTimeout,
        DriverContext context, string operation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      var byteBuffer = new byte[StreamBufferBytes];
      var charBuffer = new char[Encoding.UTF8.GetMaxCharCount(StreamBufferBytes)];
      var decoder = Encoding.UTF8.GetDecoder();
      var line = new StringBuilder();
      var sawCr = false;
      var sawBodyBytes = false;

      while (true)
      {
        var count = await ReadCharsAsync(
            stream, byteBuffer, charBuffer, decoder,
            sawBodyBytes ? idleTimeout : firstByteTimeout,
            sawBodyBytes ? "Streaming read timed out: no data received within the configured idle timeout."
                : "Streaming read timed out: no first byte received within the configured first-byte timeout.",
            context, operation, cancellationToken).ConfigureAwait(false);
        if (count == 0)
        {
          if (line.Length > 0)
            yield return line.ToString(); // final line without a trailing newline.
          yield break;
        }
        sawBodyBytes = true;

        for (var i = 0; i < count; i++)
        {
          var c = charBuffer[i];
          if (c == '\r')
          {
            yield return line.ToString();
            line.Clear();
            sawCr = true;
            continue;
          }
          if (c == '\n')
          {
            if (sawCr)
            {
              sawCr = false; // swallow the LF half of a CRLF pair (line already yielded on CR).
              continue;
            }
            yield return line.ToString();
            line.Clear();
            continue;
          }

          sawCr = false;
          if (line.Length >= MaxSseLineChars)
            throw new ModelRunnerException(
                $"SSE line exceeded the {MaxSseLineChars}-character limit",
                ErrorCodes.ModelInference.StreamParseError, CreateStreamErrorContext(context, operation));
          line.Append(c);
        }
      }
    }

    // Reads the next non-empty chunk of characters, applying a SINGLE idle-timeout window per
    // underlying read (MR5). At true EOF the stateful decoder is flushed once (a stream ending
    // mid-codepoint emits U+FFFD rather than dropping bytes); returns 0 only once that flush is
    // drained. A fired idle timeout that is NOT a caller cancellation surfaces as Timeout; caller
    // cancellation propagates as OperationCanceledException.
    // Loops past reads that decode to zero chars (a multi-byte sequence split across a chunk boundary)
    // so a partial UTF-8 char is never mistaken for EOF.
    private static async Task<int> ReadCharsAsync(
        Stream stream, byte[] byteBuffer, char[] charBuffer, Decoder decoder,
        TimeSpan? timeout, string timeoutMessage, DriverContext context, string operation,
        CancellationToken cancellationToken)
    {
      while (true)
      {
        cancellationToken.ThrowIfCancellationRequested();

        int bytesRead;
        if (timeout is null)
        {
          bytesRead = await stream.ReadAsync(byteBuffer, cancellationToken).ConfigureAwait(false);
        }
        else
        {
          using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
          idleCts.CancelAfter(timeout.Value);
          try
          {
            bytesRead = await stream.ReadAsync(byteBuffer, idleCts.Token).ConfigureAwait(false);
          }
          catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
          {
            throw new ModelRunnerException(
                timeoutMessage, ErrorCodes.ModelInference.Timeout,
                CreateStreamErrorContext(context, operation));
          }
        }

        if (bytesRead == 0)
        {
          // Real EOF: flush the stateful decoder ONCE so a stream that ends mid-codepoint emits its
          // buffered partial bytes (as U+FFFD) instead of silently dropping them. A second EOF call
          // flushes-empty (state already drained) and returns 0, ending the outer read loop.
          return decoder.GetChars(Array.Empty<byte>(), 0, 0, charBuffer, 0, flush: true);
        }

        var chars = decoder.GetChars(byteBuffer, 0, bytesRead, charBuffer, 0);
        if (chars > 0)
          return chars;
        // else: bytes buffered a partial multi-byte char — read more to complete it.
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
        if (error.ValueKind == JsonValueKind.Null)
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

    private static ErrorContext CreateStreamErrorContext(
        DriverContext context, string operation, System.Net.HttpStatusCode? statusCode = null) =>
        new(operation)
        {
          DriverId = context?.DriverId,
          Host = context?.Host,
          OperationId = context?.OperationId,
          ExitCode = statusCode.HasValue ? (int)statusCode.Value : null
        };
  }
}
