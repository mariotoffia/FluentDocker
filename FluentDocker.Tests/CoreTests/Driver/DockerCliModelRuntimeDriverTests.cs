using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
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
  /// Unit tests for <see cref="DockerCliModelRuntimeDriver"/>, with emphasis on
  /// the <c>configure</c> command assembly (context-size / reset / backend /
  /// <c>--hf_overrides</c> / <c>--</c> runtime-flag passthrough).
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class DockerCliModelRuntimeDriverTests
  {
    private static DriverContext Ctx => new("docker");

    private sealed class FakeRuntimeDriver : DockerCliModelRuntimeDriver
    {
      public List<string> Commands { get; } = [];
      public Func<string, SimpleCommandResult>? Responder { get; set; }
      public Func<string, IEnumerable<string>>? StreamResponder { get; set; }

      public FakeRuntimeDriver() : base(null!)
      {
      }

      protected override Task<SimpleCommandResult> RunAsync(string arguments, CancellationToken cancellationToken)
      {
        Commands.Add(arguments);
        // Honor the token the way a real CLI invocation would: a caller's cancellation
        // surfaces as OperationCanceledException. This lets tests prove the shared
        // `--backend` probe is not bound to (and poisoned by) a single caller's token.
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Responder?.Invoke(arguments) ?? new SimpleCommandResult { Success = true, Output = string.Empty, ExitCode = 0 });
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
    public async Task StatusAsync_Running()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok("Docker Model Runner is running\n") };
      var result = await driver.StatusAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.True(result.Data.Running);
      Assert.Contains("model status", driver.Commands.Single());
    }

    [Fact]
    public async Task StatusAsync_NotRunning()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok("Docker Model Runner is not running\n") };
      var result = await driver.StatusAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.False(result.Data.Running);
    }

    [Fact]
    public async Task StatusAsync_NonZeroExit_NotRunningShape_IsTreatedAsNotRunning()
    {
      // A non-zero exit whose output is the recognizable "not running" shape is a
      // legitimate state, not a command failure.
      var driver = new FakeRuntimeDriver
      {
        Responder = _ => new SimpleCommandResult { Success = false, ExitCode = 1, Output = "Docker Model Runner is not running\n" }
      };
      var result = await driver.StatusAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.False(result.Data.Running);
    }

    [Fact]
    public async Task StatusAsync_NonZeroExit_UnrelatedNotRunningMessage_Fails()
    {
      // A genuine command error whose text merely *contains* "not running" (e.g. an
      // unrelated daemon error about a container) must NOT be mistaken for the runner's
      // clean stopped state — only the specific "Model Runner is not running" phrase is.
      var driver = new FakeRuntimeDriver
      {
        Responder = _ => new SimpleCommandResult
        {
          Success = false,
          ExitCode = 1,
          Error = "Error response from daemon: container abc is not running"
        }
      };
      var result = await driver.StatusAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Model.StatusFailed, result.ErrorCode);
    }

    [Fact]
    public async Task StatusAsync_NonZeroExit_UnrecognizedOutput_Fails()
    {
      // A non-zero exit that is NOT a recognizable status (e.g. docker missing) must
      // surface as a failure rather than silently reporting Running=false.
      var driver = new FakeRuntimeDriver
      {
        Responder = _ => new SimpleCommandResult { Success = false, ExitCode = 127, Error = "docker: command not found", Output = string.Empty }
      };
      var result = await driver.StatusAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Model.StatusFailed, result.ErrorCode);
      Assert.Equal(127, result.ExitCode);
    }

    [Fact]
    public async Task VersionAsync_Parses()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok("Client:\n Version:    v1.2.1\n") };
      var result = await driver.VersionAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal("v1.2.1", result.Data.CliVersion);
    }

    [Fact]
    public async Task ListRunningAsync_ParsesPsTable()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok(DmrFixtures.Load("ps.txt")) };
      var result = await driver.ListRunningAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Contains(result.Data, r => r.Reference.Name == "smollm2");
      Assert.Contains("model ps", driver.Commands.Single());
    }

    [Fact]
    public async Task LogsAsync_StreamsLines_FollowFlag()
    {
      var driver = new FakeRuntimeDriver { StreamResponder = _ => new[] { "line1", "line2" } };

      var collected = new List<string>();
      await foreach (var line in driver.LogsAsync(Ctx, follow: true, TestContext.Current.CancellationToken))
        collected.Add(line);

      Assert.Equal(new[] { "line1", "line2" }, collected);
      Assert.Contains("model logs", driver.Commands.Single());
      Assert.Contains("-f", driver.Commands.Single());
    }

    [Fact]
    public async Task InstallRunnerAsync_Gpu()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.InstallRunnerAsync(Ctx, new ModelRunnerInstallOptions { Gpu = "auto" }, TestContext.Current.CancellationToken);

      var cmd = driver.Commands.Single();
      Assert.Contains("model install-runner", cmd);
      Assert.Contains("--gpu auto", cmd);
    }

    [Fact]
    public async Task UninstallRunnerAsync_ImagesAndModels()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.UninstallRunnerAsync(Ctx, new ModelRunnerUninstallOptions { RemoveImages = true, RemoveModels = true }, TestContext.Current.CancellationToken);

      var cmd = driver.Commands.Single();
      Assert.Contains("model uninstall-runner", cmd);
      Assert.Contains("--images", cmd);
      Assert.Contains("--models", cmd);
    }
  }
}
