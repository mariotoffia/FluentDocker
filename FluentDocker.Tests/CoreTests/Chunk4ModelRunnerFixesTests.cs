using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders.Compose;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Drivers.Models;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using FluentDocker.Model.Models.Options;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests
{
  [Trait("Category", "Unit")]
  [Collection("ModelEnvVars")]
  public sealed class Chunk4ModelRunnerFixesTests
  {
    private static readonly ModelReference Model = ModelReference.Parse("ai/smollm2");
    private static readonly DriverContext Ctx = new("docker");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModelApiConnection_ConnectTimeoutCancellation_MapsToEndpointUnreachable(bool finiteRequestTimeout)
    {
      using var handler = new ThrowingHandler(ConnectTimeout());
      await using var connection = finiteRequestTimeout
          ? new ModelApiConnection(new Uri("http://localhost:12434"), handler, null,
              new ModelApiConnectionConfig { RequestTimeout = TimeSpan.FromMinutes(10) })
          : new ModelApiConnection(new Uri("http://localhost:12434"), handler, null);

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(() =>
          connection.GetAsync("/models", TestContext.Current.CancellationToken));

      Assert.Equal(ErrorCodes.ModelInference.EndpointUnreachable, ex.ErrorCode);
    }

    [Fact]
    public async Task ModelApiConnection_StreamConnectTimeoutCancellation_MapsToEndpointUnreachable()
    {
      using var handler = new ThrowingHandler(ConnectTimeout());
      await using var connection = new ModelApiConnection(
          new Uri("http://localhost:12434"), handler, null,
          new ModelApiConnectionConfig { StreamFirstByteTimeout = TimeSpan.FromMinutes(10) });

      using var content = new StringContent("{}", Encoding.UTF8, "application/json");
      var ex = await Assert.ThrowsAsync<ModelRunnerException>(() =>
          connection.PostStreamAsync("/chat/completions", content, TestContext.Current.CancellationToken));

      Assert.Equal(ErrorCodes.ModelInference.EndpointUnreachable, ex.ErrorCode);
    }

    [Fact]
    public async Task ModelApiConnection_CallerCanceledSend_StaysOperationCanceled()
    {
      using var handler = new BlockingUntilCanceledHandler();
      await using var connection = new ModelApiConnection(new Uri("http://localhost:12434"), handler, null);
      using var cts = new CancellationTokenSource();

      var send = connection.GetAsync("/models", cts.Token);
      await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
      await cts.CancelAsync();
      var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);

      Assert.IsNotType<ModelRunnerException>(ex);
    }

    [Fact]
    public void ModelApiConnection_InsecureApiKeyGuard_RunsBeforeCertificateValidation()
    {
      var config = new ModelApiConnectionConfig
      {
        CertificatePath = "missing-cert.pem",
        VerifyTls = true
      };

      var ex = Assert.Throws<ModelRunnerException>(() =>
          new ModelApiConnection(
              ModelRunnerEndpoint.Custom(new Uri("http://10.0.0.5:12434")),
              config,
              apiKey: "secret"));

      Assert.Equal(ErrorCodes.ModelInference.Unauthorized, ex.ErrorCode);
    }

    [Fact]
    public async Task ModelApiConnection_DisposeAsync_DisposesHandlerOnlyOnce()
    {
      var handler = new CountingHandler();
      var connection = new ModelApiConnection(new Uri("http://localhost:12434"), handler, null);

      await connection.DisposeAsync();
      await connection.DisposeAsync();

      Assert.Equal(1, handler.DisposeCount);
    }

    [Fact]
    public async Task ChatCompletionStreamAsync_CleanEofAfterFinishReason_CompletesWithoutDone()
    {
      const string script = "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hi\"},\"finish_reason\":\"stop\"}]}\n\n";
      var connection = new ScriptConnection().SetupStream(script);
      var driver = new OpenAiModelInferenceDriver(connection, ModelRunnerEndpoint.HostTcp());
      var chunks = new List<string>();

      await foreach (var chunk in driver.ChatCompletionStreamAsync(
          Ctx, new ChatCompletionRequest { Model = "gpt-4o-mini" }, TestContext.Current.CancellationToken))
        chunks.Add(chunk.Choices![0].Delta!.Content!);

      Assert.Equal(new[] { "Hi" }, chunks);
    }

    [Fact]
    public async Task ChatCompletionStreamAsync_TwoChoicesMissingDoneAfterOneFinish_StillThrowsStreamParseError()
    {
      const string script =
          "data: {\"choices\":[" +
          "{\"index\":0,\"delta\":{\"content\":\"A\"},\"finish_reason\":\"stop\"}," +
          "{\"index\":1,\"delta\":{\"content\":\"B\"},\"finish_reason\":null}]}\n\n";
      var connection = new ScriptConnection().SetupStream(script);
      var driver = new OpenAiModelInferenceDriver(connection, ModelRunnerEndpoint.HostTcp());

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
      {
        await foreach (var _ in driver.ChatCompletionStreamAsync(
            Ctx, new ChatCompletionRequest { Model = "gpt-4o-mini" }, TestContext.Current.CancellationToken))
        {
        }
      });

      Assert.Equal(ErrorCodes.ModelInference.StreamParseError, ex.ErrorCode);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task ChatCompletionAsync_TransientHttpStatus_ReturnsServiceUnavailableCode(int statusCode)
    {
      var connection = new ScriptConnection().SetupPost((HttpStatusCode)statusCode, "cold load");
      var driver = new OpenAiModelInferenceDriver(connection, ModelRunnerEndpoint.HostTcp());

      var response = await driver.ChatCompletionAsync(
          Ctx,
          new ChatCompletionRequest
          {
            Model = "ai/smollm2",
            Messages = new List<ChatMessage> { new() { Role = "user", Content = "hi" } }
          },
          TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.Equal(ErrorCodes.ModelInference.ServiceUnavailable, response.ErrorCode);
      Assert.NotEqual(ErrorCodes.ModelInference.Timeout, response.ErrorCode);
      Assert.NotEqual(ErrorCodes.ModelInference.RequestFailed, response.ErrorCode);
      Assert.True(ErrorCodes.IsTransientCode(response.ErrorCode!));
    }

    [Fact]
    public async Task DockerCliModelManagement_PullMissingModel_ReturnsNotFound()
    {
      var driver = new FakeManagementDriver
      {
        StreamResponder = _ => Array.Empty<string>(),
        Responder = _ => new SimpleCommandResult
        {
          Success = false,
          Error = "Error: no such model: ai/missing",
          ExitCode = 1
        }
      };

      var response = await driver.PullAsync(
          Ctx, ModelReference.Parse("ai/missing"), cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.Equal(ErrorCodes.Model.NotFound, response.ErrorCode);
    }

    [Fact]
    public async Task DockerCliModelManagement_PullDriverException_PreservesTypedCode()
    {
      var driver = new FakeManagementDriver
      {
        StreamException = new DriverException("process timed out", ErrorCodes.General.Timeout)
      };

      var response = await driver.PullAsync(
          Ctx, ModelReference.Parse("ai/smollm2"), cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.Equal(ErrorCodes.General.Timeout, response.ErrorCode);
    }

    [Fact]
    public async Task ModelService_RemovedState_MakesStopAndRemoveNoOps()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var runner = new Mock<IModelRunner>();
      runner.Setup(r => r.RemoveAsync(Model, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
      runner.Setup(r => r.UnloadAsync(Model, It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
      runner.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
      var service = new ModelService(kernel, "docker", Model, runner.Object, keepRunning: true);

      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);
      await service.StopAsync(TestContext.Current.CancellationToken);
      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Removed, service.State);
      runner.Verify(r => r.RemoveAsync(Model, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
      runner.Verify(r => r.UnloadAsync(Model, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ModelService_DisposeAsync_BoundsStoppingHooks()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var runner = new Mock<IModelRunner>();
      runner.Setup(r => r.LoadAsync(Model, It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
      runner.Setup(r => r.UnloadAsync(Model, It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
      runner.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
      var service = new ModelService(
          kernel, "docker", Model, runner.Object, keepRunning: false,
          disposeCleanupTimeout: TimeSpan.FromMilliseconds(50));
      service.AddHook(ServiceRunningState.Stopping, _ => new TaskCompletionSource().Task);
      await service.StartAsync(TestContext.Current.CancellationToken);

      var disposeTask = service.DisposeAsync().AsTask();
      var completed = await Task.WhenAny(
          disposeTask,
          Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

      Assert.Same(disposeTask, completed);
      await disposeTask;
    }

    [Fact]
    public async Task ModelService_LoadTimeout_ResetsGateSoLaterStartCanRetry()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var calls = 0;
      var releaseHungLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var runner = new Mock<IModelRunner>();
      runner.Setup(r => r.LoadAsync(Model, It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .Returns(async (ModelReference _, ModelRunOptions __, CancellationToken token) =>
          {
            calls++;
            if (calls == 1)
              await releaseHungLoad.Task.ConfigureAwait(false);
          });
      runner.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
      var service = new ModelService(
          kernel, "docker", Model, runner.Object, keepRunning: true,
          loadTimeout: TimeSpan.FromMilliseconds(50));

      var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
          service.StartAsync(TestContext.Current.CancellationToken)
              .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
      Assert.Contains("model load", ex.Message, StringComparison.OrdinalIgnoreCase);
      releaseHungLoad.SetResult();

      await service.StartAsync(TestContext.Current.CancellationToken);
      Assert.Equal(2, calls);
      Assert.Equal(ServiceRunningState.Running, service.State);
    }

    [Theory]
    [InlineData("gpt-4o-mini")]
    [InlineData("ft:gpt-3.5-turbo:org:name")]
    public async Task ModelRunnerEnvironment_RemoteDefaultModel_MetadataDoesNotAddLatest(string modelId)
    {
      var runner = ModelRunnerEnvironment.CreateInferenceRunner(
          ModelRunnerEndpoint.HostTcp(), modelId);

      await using ((IAsyncDisposable)runner)
      {
        Assert.Null(((IModelRunner)runner).DefaultModel);
      }
    }

    [Fact]
    public void ComposeModelBuilder_ContextSize_RejectsNonPositiveValues()
    {
      var builder = new ComposeModelBuilder();

      var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
          builder.AddModel("llm", m => m.WithModel("ai/smollm2").WithContextSize(0)));

      Assert.Equal("tokens", ex.ParamName);
    }

    private static TaskCanceledException ConnectTimeout() =>
        new("connect timed out", new TimeoutException("connect timed out"));

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
      protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
          throw exception;
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
      public int DisposeCount { get; private set; }

      protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
          Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

      protected override void Dispose(bool disposing)
      {
        if (disposing)
          DisposeCount++;
        base.Dispose(disposing);
      }
    }

    private sealed class BlockingUntilCanceledHandler : HttpMessageHandler
    {
      public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

      protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
      {
        Started.SetResult();
        try
        {
          await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
          throw new OperationCanceledException(
              "Caller canceled while sending.",
              new TimeoutException("Timeout-shaped cancellation."),
              cancellationToken);
        }

        return new HttpResponseMessage(HttpStatusCode.OK);
      }
    }

    private sealed class ScriptConnection : IModelApiConnection
    {
      private HttpStatusCode _postStatus = HttpStatusCode.OK;
      private string _postBody = "{}";
      private string _streamBody = "";

      public Uri BaseAddress { get; } = new("http://localhost:12434");
      public TimeSpan? StreamFirstByteTimeout => null;
      public TimeSpan? StreamReadIdleTimeout => null;

      public ScriptConnection SetupPost(HttpStatusCode status, string body)
      {
        _postStatus = status;
        _postBody = body;
        return this;
      }

      public ScriptConnection SetupStream(string body)
      {
        _streamBody = body;
        return this;
      }

      public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default) =>
          Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

      public Task<HttpResponseMessage> PostAsync(string path, HttpContent content, CancellationToken ct = default) =>
          Task.FromResult(new HttpResponseMessage(_postStatus)
          {
            Content = new StringContent(_postBody, Encoding.UTF8, "application/json")
          });

      public Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct = default) =>
          Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

      public Task<Stream> PostStreamAsync(string path, HttpContent content, CancellationToken ct = default) =>
          Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(_streamBody)));

      public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(true);
      public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeManagementDriver : DockerCliModelManagementDriver
    {
      public Func<string, SimpleCommandResult>? Responder { get; init; }
      public Func<string, IEnumerable<string>>? StreamResponder { get; init; }
      public Exception? StreamException { get; init; }

      public FakeManagementDriver() : base(null!)
      {
      }

      protected override Task<SimpleCommandResult> RunAsync(
          DriverContext context, string arguments, CancellationToken cancellationToken) =>
          Task.FromResult(Responder?.Invoke(arguments) ?? new SimpleCommandResult { Success = true });

      protected override async IAsyncEnumerable<string> RunStreamingWithProgressAsync(
          DriverContext context, string arguments,
          [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
      {
        if (StreamException != null)
          throw StreamException;

        foreach (var line in StreamResponder?.Invoke(arguments) ?? [])
        {
          cancellationToken.ThrowIfCancellationRequested();
          yield return line;
          await Task.CompletedTask;
        }
      }
    }
  }
}
