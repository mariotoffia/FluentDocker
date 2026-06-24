using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Unit tests for <see cref="DockerCliModelManagementDriver"/>: command-string
  /// assembly + parsed results via a fake executor seam (no real <c>docker</c>).
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerCliModelManagementDriverTests
  {
    private static DriverContext Ctx => new("docker");

    /// <summary>A test double that captures commands and replays canned results.</summary>
    private sealed class FakeMgmtDriver : DockerCliModelManagementDriver
    {
      public List<string> Commands { get; } = [];
      public Func<string, SimpleCommandResult> Responder { get; set; }
      public Func<string, IEnumerable<string>> StreamResponder { get; set; }

      public FakeMgmtDriver() : base(null)
      {
      }

      protected override Task<SimpleCommandResult> RunAsync(string arguments, CancellationToken cancellationToken)
      {
        Commands.Add(arguments);
        var result = Responder?.Invoke(arguments) ?? new SimpleCommandResult { Success = true, Output = string.Empty, ExitCode = 0 };
        return Task.FromResult(result);
      }

      protected override IAsyncEnumerable<string> RunStreamingAsync(string arguments, CancellationToken cancellationToken)
      {
        Commands.Add(arguments);
        return ToAsync(StreamResponder?.Invoke(arguments) ?? Array.Empty<string>());
      }

      private static async IAsyncEnumerable<string> ToAsync(IEnumerable<string> items)
      {
        await Task.CompletedTask;
        foreach (var item in items)
          yield return item;
      }
    }

    private static SimpleCommandResult Ok(string output = "") => new() { Success = true, Output = output, ExitCode = 0 };

    [Fact]
    public async Task ListAsync_EmitsLsJson_AndParses()
    {
      var driver = new FakeMgmtDriver { Responder = _ => Ok(DmrFixtures.Load("ls.json")) };

      var result = await driver.ListAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Contains("model ls --json", driver.Commands.Single());
      Assert.Contains(result.Data, m => m.Reference.Name == "smollm2");
    }

    [Fact]
    public async Task ListAsync_MalformedJsonDespiteZeroExit_Fails()
    {
      // A zero-exit run that prints non-JSON (e.g. a warning) must surface as a failure,
      // not a silent "zero models" success.
      var driver = new FakeMgmtDriver { Responder = _ => Ok("WARNING: something went wrong, not json") };

      var result = await driver.ListAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Model.ListFailed, result.ErrorCode);
    }

    [Fact]
    public async Task ListAsync_EmptyOutput_IsSuccessfulEmptyList()
    {
      var driver = new FakeMgmtDriver { Responder = _ => Ok(string.Empty) };

      var result = await driver.ListAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Empty(result.Data);
    }

    [Fact]
    public async Task InspectAsync_EmitsInspectWithoutJsonFlag_AndParses()
    {
      var driver = new FakeMgmtDriver { Responder = _ => Ok(DmrFixtures.Load("inspect.json")) };

      var result = await driver.InspectAsync(Ctx, ModelReference.Parse("ai/smollm2"), TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal("smollm2", result.Data.Reference.Name);
      var cmd = driver.Commands.Single();
      Assert.Contains("model inspect", cmd);
      Assert.DoesNotContain("--json", cmd); // DMR v1.2.1 inspect rejects --json
    }

    [Fact]
    public async Task ListAsync_Failure_ReturnsFailWithContext()
    {
      var driver = new FakeMgmtDriver
      {
        Responder = _ => new SimpleCommandResult { Success = false, Error = "boom", ExitCode = 1 }
      };

      var result = await driver.ListAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Model.ListFailed, result.ErrorCode);
      Assert.NotNull(result.ErrorContext);
      Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task RemoveAsync_ForceFlag_AndQuotedRef()
    {
      var driver = new FakeMgmtDriver { Responder = _ => Ok() };

      var result = await driver.RemoveAsync(Ctx, ModelReference.Parse("ai/smollm2"), force: true, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      var cmd = driver.Commands.Single();
      Assert.Contains("model rm", cmd);
      Assert.Contains("-f", cmd);
      Assert.Contains("ai/smollm2", cmd);
    }

    [Fact]
    public async Task RemoveAsync_NoSuchModel_DetectedFromOutputDespiteZeroExit()
    {
      // DMR rm prints "no such model" but exits 0 — must be detected from output.
      var driver = new FakeMgmtDriver
      {
        Responder = _ => Ok("Failed to remove model: no such model: ai/x")
      };

      var result = await driver.RemoveAsync(Ctx, ModelReference.Parse("ai/x"), false, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Model.RemoveFailed, result.ErrorCode);
    }

    [Fact]
    public async Task TagAsync_EmitsTagCommand()
    {
      var driver = new FakeMgmtDriver { Responder = _ => Ok() };

      await driver.TagAsync(Ctx, ModelReference.Parse("ai/a"), ModelReference.Parse("ai/b:v2"), TestContext.Current.CancellationToken);

      var cmd = driver.Commands.Single();
      Assert.Contains("model tag", cmd);
      Assert.Contains("ai/a", cmd);
      Assert.Contains("ai/b:v2", cmd);
    }

    [Fact]
    public async Task PushAsync_EmitsPushCommand()
    {
      var driver = new FakeMgmtDriver { Responder = _ => Ok() };
      await driver.PushAsync(Ctx, ModelReference.Parse("ai/a"), TestContext.Current.CancellationToken);
      Assert.Contains("model push", driver.Commands.Single());
    }

    [Fact]
    public async Task PackageAsync_EmitsGgufAndPush()
    {
      var driver = new FakeMgmtDriver { Responder = _ => Ok() };

      await driver.PackageAsync(Ctx, new ModelPackageRequest
      {
        GgufPath = "/tmp/m.gguf",
        Target = ModelReference.Parse("ai/mine:1"),
        Push = true
      }, TestContext.Current.CancellationToken);

      var cmd = driver.Commands.Single();
      Assert.Contains("model package", cmd);
      Assert.Contains("--gguf", cmd);
      Assert.Contains("/tmp/m.gguf", cmd);
      Assert.Contains("--push", cmd);
      Assert.Contains("ai/mine:1", cmd);
    }

    [Fact]
    public async Task PackageAsync_EmitsLicense_NeverUnsupportedLabel()
    {
      // `docker model package` supports `--license <path>`, NOT `--label`. The
      // license must be emitted and the (unsupported) label flag must never appear.
      var driver = new FakeMgmtDriver { Responder = _ => Ok() };

      await driver.PackageAsync(Ctx, new ModelPackageRequest
      {
        GgufPath = "/tmp/m.gguf",
        Target = ModelReference.Parse("ai/mine:1"),
        License = "/tmp/LICENSE.txt"
      }, TestContext.Current.CancellationToken);

      var cmd = driver.Commands.Single();
      Assert.Contains("--license", cmd);
      Assert.Contains("/tmp/LICENSE.txt", cmd);
      Assert.DoesNotContain("--label", cmd);
    }

    [Fact]
    public async Task PurgeAllAsync_EmitsPurgeForce_NeverEmitsPrune()
    {
      // DMR v1.2.1 has no `model prune` (it prints top-level help and exits 0, a
      // false success). The real verb is `model purge --force`.
      var driver = new FakeMgmtDriver { Responder = _ => Ok("Removed 2 models, reclaimed 1.5 GB") };

      var result = await driver.PurgeAllAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      var cmd = driver.Commands.Single();
      Assert.Contains("model purge", cmd);
      Assert.Contains("--force", cmd);
      Assert.DoesNotContain("model prune", cmd);
      Assert.Equal("Removed 2 models, reclaimed 1.5 GB", result.Data.RawOutput);
    }

    [Fact]
    public async Task DiskUsageAsync_ParsesTable()
    {
      var driver = new FakeMgmtDriver { Responder = _ => Ok(DmrFixtures.Load("df.txt")) };

      var result = await driver.DiskUsageAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.True(result.Data.ModelsSizeBytes > 0);
      var cmd = driver.Commands.Single();
      Assert.Contains("model df", cmd);
      Assert.DoesNotContain("--json", cmd); // df rejects --json
    }

    [Fact]
    public async Task ListAsync_Cancellation_Propagates()
    {
      var driver = new FakeMgmtDriver { Responder = _ => throw new OperationCanceledException() };

      await Assert.ThrowsAsync<OperationCanceledException>(() => driver.ListAsync(Ctx, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PullAsync_StreamsProgress_ThenReturnsInspectedInfo()
    {
      // The stream below yields 3 lines, each parsing to a non-null progress update.
      var progress = new CapturingProgress<ModelPullProgress>(expected: 3);

      var driver = new FakeMgmtDriver
      {
        StreamResponder = _ => new[] { "Downloaded 100MB of 200MB", "Downloaded 200MB of 200MB", "Model pulled successfully" },
        Responder = args => args.Contains("inspect") ? Ok(DmrFixtures.Load("inspect.json")) : Ok()
      };

      var result = await driver.PullAsync(Ctx, ModelReference.Parse("ai/smollm2"), progress, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal("smollm2", result.Data.Reference.Name);
      Assert.Contains(driver.Commands, c => c.Contains("model pull"));

      // Progress is reported asynchronously (callbacks may be posted to the thread pool),
      // so wait deterministically for the expected reports instead of sleeping.
      var progressEvents = await progress.WaitForReportsAsync(TestContext.Current.CancellationToken);
      Assert.Equal(3, progressEvents.Count);
      Assert.NotEmpty(progressEvents);
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
