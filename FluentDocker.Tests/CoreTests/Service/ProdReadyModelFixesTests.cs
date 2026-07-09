using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Kernel;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public sealed class ProdReadyModelFixesTests
  {
    private static readonly ModelReference Model = ModelReference.Parse("ai/smollm2");

    [Fact]
    public async Task StartAsync_DisposeDuringSharedLoad_FaultsWaitingCaller()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var runner = new Mock<IModelRunner>();
      runner.Setup(r => r.LoadAsync(
              It.IsAny<ModelReference>(), It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .Returns(async (ModelReference _, ModelRunOptions __, CancellationToken token) =>
          {
            loadStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
          });
      runner.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
      var service = new ModelService(kernel, "docker", Model, runner.Object, null!, keepRunning: true);

      var first = service.StartAsync(TestContext.Current.CancellationToken);
      await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      var waiting = service.StartAsync(TestContext.Current.CancellationToken);
      await service.DisposeAsync();

      AssertDisposedOrCanceled(await Assert.ThrowsAnyAsync<Exception>(() =>
          waiting.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)));
      AssertDisposedOrCanceled(await Assert.ThrowsAnyAsync<Exception>(() =>
          first.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task ModelRunnerService_PerModelOperations_NullModelThrowsArgumentNullException()
    {
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync(
          "docker", new MockDriverPack().EnableModelDrivers());
      await using (kernel)
      {
        var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(), Model);

        await AssertArgumentNullAsync("model", token => runner.PullAsync(null!, null, token));
        await AssertArgumentNullAsync("model", token => runner.InspectAsync(null!, token));
        await AssertArgumentNullAsync("model", token => runner.RemoveAsync(null!, false, token));
        await AssertArgumentNullAsync("source", token => runner.TagAsync(null!, Model, token));
        await AssertArgumentNullAsync("target", token => runner.TagAsync(Model, null!, token));
        await AssertArgumentNullAsync("model", token => runner.PushAsync(null!, token));
        await AssertArgumentNullAsync("model", token => runner.LoadAsync(null!, null, token));
        await AssertArgumentNullAsync("model", token => runner.UnloadAsync(null!, token));
        await AssertArgumentNullAsync("model", token => runner.ConfigureAsync(null!, new ModelConfigureOptions(), token));
      }
    }

    [Fact]
    public async Task PostStreamAsync_ByteArrayReadAsync_UsesAsyncCancelableReadPath()
    {
      using var handler = new SingleResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StreamContent(new AsyncOnlyStallingStream())
      });
      await using var connection = new ModelApiConnection(new Uri("http://localhost:12434"), handler, null);
      using var body = new StringContent("{}", Encoding.UTF8, "application/json");
      await using var stream = await connection.PostStreamAsync(
          "/chat/completions", body, TestContext.Current.CancellationToken);
      using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
      var buffer = new byte[1];

#pragma warning disable CA1835 // This test intentionally exercises the classic byte[] overload.
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
          stream.ReadAsync(buffer, 0, buffer.Length, cts.Token)
              .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
#pragma warning restore CA1835
      Assert.True(cts.IsCancellationRequested);
    }

    private static void AssertDisposedOrCanceled(Exception ex) =>
        Assert.True(
            ex is ObjectDisposedException or OperationCanceledException,
            $"Expected disposed/canceled shared-load failure, got {ex.GetType().FullName}: {ex.Message}");

    private static async Task AssertArgumentNullAsync(string paramName, Func<CancellationToken, Task> action)
    {
      var ex = await Assert.ThrowsAsync<ArgumentNullException>(
          () => action(TestContext.Current.CancellationToken));
      Assert.Equal(paramName, ex.ParamName);
    }

    private sealed class SingleResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
      protected override Task<HttpResponseMessage> SendAsync(
          HttpRequestMessage request, CancellationToken cancellationToken) =>
          Task.FromResult(response);
    }

    private sealed class AsyncOnlyStallingStream : Stream
    {
      public override bool CanRead => true;
      public override bool CanSeek => false;
      public override bool CanWrite => false;
      public override long Length => throw new NotSupportedException();
      public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

      public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
      {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        return 0;
      }

      public override int Read(byte[] buffer, int offset, int count) =>
          throw new NotSupportedException("Synchronous Read was used instead of async ReadAsync.");

      public override void Flush()
      {
      }

      public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
      public override void SetLength(long value) => throw new NotSupportedException();
      public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
  }
}
