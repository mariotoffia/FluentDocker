using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Models;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  [Trait("Category", "Unit")]
  public partial class OpenAiModelInferenceStreamingTests
  {
    private static DriverContext DiagnosticCtx => new("docker", "dmr.example");

    [Fact]
    public async Task ChatCompletionStreamAsync_Http404_AttachesErrorContext()
    {
      var conn = new MockModelApiConnection().SetupStreamStatus("/chat/completions", 404, "model not found");
      var driver = Create(conn);

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
      {
        await foreach (var _ in driver.ChatCompletionStreamAsync(
            DiagnosticCtx, new ChatCompletionRequest { Model = "ai/nope" }, TestContext.Current.CancellationToken))
        {
        }
      });

      Assert.Equal(ErrorCodes.ModelInference.ModelNotLoaded, ex.ErrorCode);
      Assert.Equal("docker", ex.Context.DriverId);
      Assert.Equal("dmr.example", ex.Context.Host);
      Assert.Equal(404, ex.Context.ExitCode);
      Assert.Equal("ChatCompletionStream", ex.Context.Operation);
    }

    [Fact]
    public async Task ChatCompletionStreamAsync_MidStreamErrorFrame_AttachesErrorContext()
    {
      const string script = "data: {\"error\":{\"message\":\"context length exceeded\"}}\n\n";
      var conn = new MockModelApiConnection().SetupStream("/chat/completions", script);
      var driver = Create(conn);

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
      {
        await foreach (var _ in driver.ChatCompletionStreamAsync(
            DiagnosticCtx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken))
        {
        }
      });

      Assert.Equal(ErrorCodes.ModelInference.RequestFailed, ex.ErrorCode);
      Assert.Equal("docker", ex.Context.DriverId);
      Assert.Equal("dmr.example", ex.Context.Host);
      Assert.Equal("ChatCompletionStream", ex.Context.Operation);
    }

    [Fact]
    public async Task ChatCompletionStreamAsync_OpenTimeout_AttachesErrorContext()
    {
      var conn = new ThrowingStreamConnection(new ModelRunnerException(
          "open timed out", ErrorCodes.ModelInference.Timeout));
      var driver = new OpenAiModelInferenceDriver(conn, ModelRunnerEndpoint.HostTcp());

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
      {
        await foreach (var _ in driver.ChatCompletionStreamAsync(
            DiagnosticCtx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken))
        {
        }
      });

      Assert.Equal(ErrorCodes.ModelInference.Timeout, ex.ErrorCode);
      Assert.Equal("docker", ex.Context.DriverId);
      Assert.Equal("dmr.example", ex.Context.Host);
      Assert.Equal("ChatCompletionStream", ex.Context.Operation);
    }

    [Fact]
    public async Task ChatCompletionStreamAsync_FirstBodyByte_UsesFirstByteTimeout()
    {
      var conn = new MockModelApiConnection
      {
        StreamFirstByteTimeout = TimeSpan.FromMilliseconds(100),
        StreamReadIdleTimeout = TimeSpan.FromSeconds(30)
      };
      conn.SetupStreamStalling("/chat/completions");
      var driver = Create(conn);

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
      {
        await foreach (var _ in driver.ChatCompletionStreamAsync(
            DiagnosticCtx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken))
        {
        }
      });

      Assert.Equal(ErrorCodes.ModelInference.Timeout, ex.ErrorCode);
      Assert.Contains("first byte", ex.Message, StringComparison.OrdinalIgnoreCase);
      Assert.Equal("ChatCompletionStream", ex.Context.Operation);
    }

    [Fact]
    public async Task ChatCompletionStreamAsync_SubsequentStall_UsesIdleTimeout()
    {
      var chunks = new List<string>();
      var conn = new MockModelApiConnection
      {
        StreamFirstByteTimeout = TimeSpan.FromSeconds(30),
        StreamReadIdleTimeout = TimeSpan.FromMilliseconds(100)
      };
      conn.SetupStreamStalling(
          "/chat/completions",
          "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hi\"}}]}\n\n");
      var driver = Create(conn);

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
      {
        await foreach (var chunk in driver.ChatCompletionStreamAsync(
            DiagnosticCtx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken))
          chunks.Add(chunk.Choices[0].Delta.Content);
      });

      Assert.Equal(new[] { "Hi" }, chunks);
      Assert.Equal(ErrorCodes.ModelInference.Timeout, ex.ErrorCode);
      Assert.Contains("idle timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ThrowingStreamConnection(ModelRunnerException exception) : IModelApiConnection
    {
      public Uri BaseAddress => new("http://localhost:12434");
      public TimeSpan? StreamFirstByteTimeout => null;
      public TimeSpan? StreamReadIdleTimeout => null;
      public Task<System.Net.Http.HttpResponseMessage> GetAsync(string path, CancellationToken ct = default) =>
          throw new NotSupportedException();
      public Task<System.Net.Http.HttpResponseMessage> PostAsync(
          string path, System.Net.Http.HttpContent content, CancellationToken ct = default) =>
          throw new NotSupportedException();
      public Task<System.Net.Http.HttpResponseMessage> DeleteAsync(string path, CancellationToken ct = default) =>
          throw new NotSupportedException();
      public Task<System.IO.Stream> PostStreamAsync(
          string path, System.Net.Http.HttpContent content, CancellationToken ct = default) =>
          throw exception;
      public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(false);
      public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
  }
}
