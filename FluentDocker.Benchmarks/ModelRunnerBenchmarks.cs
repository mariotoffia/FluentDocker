using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;

namespace FluentDocker.Benchmarks
{
  /// <summary>
  /// Benchmarks for the Docker Model Runner subsystem: model-reference parsing,
  /// chat (de)serialization, SSE chunk decoding throughput, and an end-to-end
  /// replayed-SSE simulation through the inference driver (deterministic — no DMR).
  /// </summary>
  [MemoryDiagnoser]
  public class ModelRunnerBenchmarks : IAsyncDisposable
  {
    private const string ChatJson =
        "{\"choices\":[{\"finish_reason\":\"length\",\"index\":0,\"message\":{\"role\":\"assistant\"," +
        "\"content\":\"Hello! My name is SmolLM, and I am here to help.\"}}],\"created\":1782191248," +
        "\"model\":\"model.gguf\",\"object\":\"chat.completion\",\"usage\":{\"completion_tokens\":12," +
        "\"prompt_tokens\":36,\"total_tokens\":48}}";

    private const string ChunkJson =
        "{\"choices\":[{\"finish_reason\":null,\"index\":0,\"delta\":{\"content\":\"Hello\"}}]," +
        "\"created\":1782191248,\"id\":\"chatcmpl-x\",\"model\":\"model.gguf\",\"object\":\"chat.completion.chunk\"}";

    private ChatCompletionRequest _request = null!;
    private string _sseScript = null!;
    private FixedStreamConnection _connection = null!;
    private DockerApiModelInferenceDriver _driver = null!;

    [GlobalSetup]
    public void Setup()
    {
      _request = new ChatCompletionRequest
      {
        Model = "ai/smollm2",
        Messages = new List<ChatMessage>
        {
          new() { Role = "system", Content = "You are helpful." },
          new() { Role = "user", Content = "Hello" }
        },
        MaxTokens = 256,
        Temperature = 0.7
      };

      var sb = new StringBuilder();
      for (var i = 0; i < 64; i++)
        sb.Append("data: ").Append(ChunkJson).Append("\n\n");
      sb.Append("data: [DONE]\n\n");
      _sseScript = sb.ToString();

      _connection = new FixedStreamConnection(_sseScript);
      _driver = new DockerApiModelInferenceDriver(_connection, ModelRunnerEndpoint.HostTcp());
    }

    [GlobalCleanup]
    public async Task Cleanup() => await DisposeAsync().ConfigureAwait(false);

    /// <summary>Disposes the replay connection owned by the benchmark.</summary>
    public async ValueTask DisposeAsync()
    {
      if (_connection is not null)
        await _connection.DisposeAsync().ConfigureAwait(false);
      GC.SuppressFinalize(this);
    }

    [Benchmark(Description = "ModelReference.Parse (hf.co)")]
    public ModelReference ParseModelReference() => ModelReference.Parse("hf.co/bartowski/Llama-3.2:Q4_K_M");

    [Benchmark(Description = "Deserialize chat completion response")]
    public ChatCompletionResponse DeserializeChatResponse() => JsonHelper.TryDeserialize<ChatCompletionResponse>(ChatJson);

    [Benchmark(Description = "Serialize chat completion request")]
    public string SerializeChatRequest() => JsonHelper.Serialize(_request);

    [Benchmark(Description = "Decode 64 SSE chunks")]
    public int DecodeSseChunks()
    {
      var count = 0;
      foreach (var line in _sseScript.Split('\n'))
      {
        if (line.Length == 0 || !line.StartsWith("data:", StringComparison.Ordinal))
          continue;
        var payload = line.AsSpan(5).Trim();
        if (payload.SequenceEqual("[DONE]"))
          break;
        var chunk = System.Text.Json.JsonSerializer.Deserialize<ChatCompletionChunk>(payload, JsonHelper.CaseInsensitiveOptions);
        if (chunk != null)
          count++;
      }

      return count;
    }

    [Benchmark(Description = "Replay SSE stream through inference driver")]
    public async Task<int> ReplaySseStream()
    {
      var count = 0;
      await foreach (var chunk in _driver.ChatCompletionStreamAsync(new DriverContext("bench"), _request, CancellationToken.None))
      {
        if (chunk.Choices is { Count: > 0 })
          count++;
      }

      return count;
    }

    /// <summary>A minimal connection that replays a fixed SSE script for PostStream.</summary>
    private sealed class FixedStreamConnection(string script) : IModelApiConnection
    {
      private readonly byte[] _bytes = Encoding.UTF8.GetBytes(script);

      public Uri BaseAddress => new("http://localhost:12434");

      public TimeSpan? StreamReadIdleTimeout => null;

      public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default) =>
          Task.FromResult(new HttpResponseMessage());

      public Task<HttpResponseMessage> PostAsync(string path, HttpContent content, CancellationToken ct = default) =>
          Task.FromResult(new HttpResponseMessage());

      public Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct = default) =>
          Task.FromResult(new HttpResponseMessage());

      public Task<Stream> PostStreamAsync(string path, HttpContent content, CancellationToken ct = default) =>
          Task.FromResult<Stream>(new MemoryStream(_bytes));

      public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(true);

      public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
  }
}
