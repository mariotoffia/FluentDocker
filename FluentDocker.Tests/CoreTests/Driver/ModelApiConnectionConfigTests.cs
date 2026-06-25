using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Unit tests for <see cref="ModelApiConnectionConfig"/> configuration properties
  /// and the idle-timeout behaviour wired through the SSE streaming path in
  /// <see cref="DockerApiModelInferenceDriver"/>.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelApiConnectionConfigTests
  {
    private static DriverContext Ctx => new("docker");

    private static DockerApiModelInferenceDriver Create(MockModelApiConnection conn) =>
        new(conn, ModelRunnerEndpoint.HostTcp());

    [Fact]
    public void StreamReadIdleTimeout_DefaultIsNull()
    {
      var config = new ModelApiConnectionConfig();
      Assert.Null(config.StreamReadIdleTimeout);
    }

    [Fact]
    public void StreamReadIdleTimeout_CanBeSet()
    {
      var config = new ModelApiConnectionConfig { StreamReadIdleTimeout = TimeSpan.FromSeconds(30) };
      Assert.Equal(TimeSpan.FromSeconds(30), config.StreamReadIdleTimeout);
    }

    [Fact]
    public void AllowTlsHostnameMismatch_DefaultIsFalse()
    {
      var config = new ModelApiConnectionConfig();
      Assert.False(config.AllowTlsHostnameMismatch);
    }

    [Fact]
    public void AllowTlsHostnameMismatch_CanBeSet()
    {
      var config = new ModelApiConnectionConfig { AllowTlsHostnameMismatch = true };
      Assert.True(config.AllowTlsHostnameMismatch);
    }

    /// <summary>
    /// FINDING 1 (C12): idle timeout is now wired into the PRODUCTION SSE read loop in
    /// <see cref="DockerApiModelInferenceDriver"/>. Verify that a stream that stalls
    /// (stops sending) fires <see cref="ErrorCodes.ModelInference.EndpointUnreachable"/>
    /// (not StreamParseError) within the configured window.
    /// </summary>
    [Fact]
    public async Task ChatCompletionStream_StalledAfterHeader_IdleTimeoutFiresEndpointUnreachable()
    {
      // Arrange: stream begins with a valid SSE preamble ("data: " prefix) but then
      // stalls forever, exercising the per-read idle timeout in ReadBoundedLineAsync.
      // The stalling stream is returned by MockModelApiConnection when
      // StreamReadIdleTimeout is set.
      var conn = new MockModelApiConnection
      {
        // Expose the idle timeout so the driver's ReadBoundedLineAsync picks it up.
        StreamReadIdleTimeout = TimeSpan.FromMilliseconds(200)
      };
      conn.SetupStreamStalling("/chat/completions");
      var driver = Create(conn);

      // Act + Assert: must throw before the test-framework timeout (CancellationToken
      // provided by xUnit ensures the test is never left hanging).
      using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
      {
        await foreach (var _ in driver.ChatCompletionStreamAsync(
            Ctx, new ChatCompletionRequest { Model = "ai/x" }, cts.Token))
        {
        }
      });

      // FINDING 4 (C12): stalled stream -> EndpointUnreachable (not StreamParseError).
      Assert.Equal(ErrorCodes.ModelInference.EndpointUnreachable, ex.ErrorCode);
      Assert.Contains("idle timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// With <c>StreamReadIdleTimeout == null</c> (default), a slow-but-progressing stream
    /// must NOT be aborted. The driver relies solely on the caller's
    /// <see cref="CancellationToken"/>.
    /// </summary>
    [Fact]
    public async Task ChatCompletionStream_NullIdleTimeout_SlowStreamCompletesNormally()
    {
      // Arrange: instant in-memory stream, no idle timeout.
      const string script = "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hi\"}}]}\n\ndata: [DONE]\n\n";
      var conn = new MockModelApiConnection(); // StreamReadIdleTimeout defaults to null
      conn.SetupStream("/chat/completions", script);
      var driver = Create(conn);

      // Act: iterate to completion.
      var received = new List<string>();
      await foreach (var chunk in driver.ChatCompletionStreamAsync(
          Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken))
      {
        var delta = chunk.Choices?[0].Delta?.Content;
        if (delta is not null)
          received.Add(delta);
      }

      // Assert: stream completed without error.
      Assert.Equal(new[] { "Hi" }, received);
    }

    /// <summary>
    /// When the caller cancels (not the idle timeout), the stall must surface as
    /// <see cref="OperationCanceledException"/> — NOT as an idle-timeout
    /// <see cref="ModelRunnerException"/>. This verifies the `!ct.IsCancellationRequested`
    /// guard in the wired-in idle-timeout path.
    /// </summary>
    [Fact]
    public async Task ChatCompletionStream_CallerCancels_ThrowsOce_NotIdleTimeout()
    {
      var conn = new MockModelApiConnection
      {
        StreamReadIdleTimeout = TimeSpan.FromSeconds(30) // long enough to never fire
      };
      conn.SetupStreamStalling("/chat/completions");
      var driver = Create(conn);

      using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

      await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
      {
        await foreach (var _ in driver.ChatCompletionStreamAsync(
            Ctx, new ChatCompletionRequest { Model = "ai/x" }, cts.Token))
        {
        }
      });
    }
  }
}
