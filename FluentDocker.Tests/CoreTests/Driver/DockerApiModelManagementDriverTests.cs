using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Unit tests for <see cref="DockerApiModelManagementDriver"/> (native
  /// <c>/models*</c> endpoints) via <see cref="MockModelApiConnection"/>, using real
  /// DMR native payloads.
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerApiModelManagementDriverTests
  {
    private static DriverContext Ctx => new("docker");

    private static DockerApiModelManagementDriver Create(MockModelApiConnection conn) => new(conn);

    [Fact]
    public async Task ListAsync_GetsModels_AndParses()
    {
      var conn = new MockModelApiConnection().SetupGet("/models", 200, DmrFixtures.Load("models-native.json"));
      var result = await Create(conn).ListAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Contains(result.Data, m => m.Reference.Name == "smollm2");
      Assert.Contains(conn.GetRequests(), r => r.Method == "GET" && r.Path == "/models");
    }

    [Fact]
    public async Task InspectAsync_GetsNamespacedPath()
    {
      var conn = new MockModelApiConnection().SetupGet("/models/ai/smollm2", 200, DmrFixtures.Load("inspect.json"));
      var result = await Create(conn).InspectAsync(Ctx, ModelReference.Parse("ai/smollm2"), TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal("smollm2", result.Data.Reference.Name);
      Assert.Contains(conn.GetRequests(), r => r.Method == "GET" && r.Path == "/models/ai/smollm2");
    }

    [Fact]
    public async Task RemoveAsync_DeletesNamespacedPath()
    {
      var conn = new MockModelApiConnection().SetupDelete("/models/ai/smollm2", 200, "{}");
      var result = await Create(conn).RemoveAsync(Ctx, ModelReference.Parse("ai/smollm2"), false, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Contains(conn.GetRequests(), r => r.Method == "DELETE" && r.Path == "/models/ai/smollm2");
    }

    [Fact]
    public async Task RemoveAsync_404_Fails()
    {
      var conn = new MockModelApiConnection().SetupDelete("/models/ai/x", 404, "{\"message\":\"not found\"}");
      var result = await Create(conn).RemoveAsync(Ctx, ModelReference.Parse("ai/x"), false, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Model.RemoveFailed, result.ErrorCode);
    }

    [Fact]
    public async Task PullAsync_PostsCreate_StreamsProgress_ReturnsInspected()
    {
      var conn = new MockModelApiConnection()
          .SetupStream("/models/create", DmrFixtures.Load("create.ndjson"))
          .SetupGet("/models/ai/smollm2", 200, DmrFixtures.Load("inspect.json"));

      // create.ndjson carries 3 progress lines, each parsing to a non-null update.
      var progress = new CapturingProgress<ModelPullProgress>(expected: 3);

      var result = await Create(conn).PullAsync(Ctx, ModelReference.Parse("ai/smollm2"), progress, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal("smollm2", result.Data.Reference.Name);
      Assert.Contains(conn.GetRequests(), r => r.Method == "POST_STREAM" && r.Path.Contains("/models/create"));

      // Progress<T>-style callbacks may post to the thread pool, so wait deterministically
      // for the expected number of reports instead of sleeping a fixed interval.
      var events = await progress.WaitForReportsAsync(TestContext.Current.CancellationToken);
      Assert.Equal(3, events.Count);
      Assert.NotEmpty(events);
    }

    [Fact]
    public async Task UnsupportedOperations_Throw()
    {
      var conn = new MockModelApiConnection();
      var driver = Create(conn);

      await Assert.ThrowsAsync<NotSupportedException>(() => driver.TagAsync(Ctx, ModelReference.Parse("ai/a"), ModelReference.Parse("ai/b")));
      await Assert.ThrowsAsync<NotSupportedException>(() => driver.PushAsync(Ctx, ModelReference.Parse("ai/a")));
      await Assert.ThrowsAsync<NotSupportedException>(() => driver.PackageAsync(Ctx, new ModelPackageRequest()));
      await Assert.ThrowsAsync<NotSupportedException>(() => driver.PurgeAllAsync(Ctx));
      await Assert.ThrowsAsync<NotSupportedException>(() => driver.DiskUsageAsync(Ctx));
    }

    /// <summary>
    /// An <see cref="IProgress{T}"/> capture that records reports into a thread-safe
    /// list and signals completion deterministically once the expected number of
    /// reports has arrived. <see cref="Progress{T}"/> dispatches callbacks via the
    /// captured <see cref="SynchronizationContext"/> (or the thread pool when none),
    /// so tests must await the signal rather than sleep for a fixed interval.
    /// </summary>
    private sealed class CapturingProgress<T> : IProgress<T>
    {
      private readonly List<T> _reports = new();
      private readonly TaskCompletionSource<bool> _completed =
          new(TaskCreationOptions.RunContinuationsAsynchronously);
      private readonly int _expected;
      private readonly Progress<T> _inner;

      public CapturingProgress(int expected)
      {
        _expected = expected;
        // Use a real Progress<T> as the reporting source to mirror production
        // behavior (callbacks may be posted to the thread pool).
        _inner = new Progress<T>(OnReport);
      }

      public void Report(T value) => ((IProgress<T>)_inner).Report(value);

      private void OnReport(T value)
      {
        lock (_reports)
        {
          _reports.Add(value);
          if (_reports.Count >= _expected)
            _completed.TrySetResult(true);
        }
      }

      /// <summary>
      /// Waits for the expected number of reports (with a generous failsafe timeout
      /// that fails the test if hit) and returns a snapshot of the captured reports.
      /// </summary>
      public async Task<IReadOnlyList<T>> WaitForReportsAsync(CancellationToken cancellationToken)
      {
        var failsafe = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        var winner = await Task.WhenAny(_completed.Task, failsafe).ConfigureAwait(false);
        Assert.True(winner == _completed.Task,
            $"Timed out waiting for {_expected} progress report(s).");

        lock (_reports)
          return _reports.ToList();
      }
    }
  }
}
