using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  [Trait("Category", "Unit")]
  public partial class DockerCliModelRuntimeDriverTests
  {
    [Fact]
    public async Task LoadAsync_DetachedWithDebug_NeverEmitsUnsupportedFlags()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.LoadAsync(Ctx, ModelReference.Parse("ai/smollm2"),
          new ModelRunOptions { Debug = true }, TestContext.Current.CancellationToken);

      var cmd = driver.Commands.Single();
      Assert.Contains("model run", cmd);
      Assert.Contains("-d", cmd);
      Assert.Contains("--debug", cmd);
      Assert.Contains("ai/smollm2", cmd);
      // `docker model run` v1.2.1 exposes neither of these — emitting them fails the command.
      Assert.DoesNotContain("--ignore-runtime-memory-check", cmd);
      Assert.DoesNotContain("--backend", cmd);
    }

    [Fact]
    public async Task UnloadAllAsync_All()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.UnloadAllAsync(Ctx, TestContext.Current.CancellationToken);
      Assert.Contains("--all", driver.Commands.Single());
    }

    [Fact]
    public async Task UnloadAsync_Single()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.UnloadAsync(Ctx, ModelReference.Parse("ai/smollm2"), TestContext.Current.CancellationToken);
      var cmd = driver.Commands.Single();
      Assert.Contains("model unload", cmd);
      Assert.Contains("ai/smollm2", cmd);
      Assert.DoesNotContain("--all", cmd);
    }

    // ---- NEW8: LoadAsync honors the full ModelRunOptions contract ----

    [Fact]
    public async Task LoadAsync_RunSupportedFields_MapToRunFlags()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.LoadAsync(Ctx, ModelReference.Parse("ai/smollm2"),
          new ModelRunOptions { Debug = true, WebSearch = true, OpenAiUrl = "http://localhost:9000/v1" },
          TestContext.Current.CancellationToken);

      var cmd = driver.Commands.Single();
      Assert.Contains("model run", cmd);
      Assert.Contains("-d", cmd);
      Assert.Contains("--debug", cmd);
      Assert.Contains("--websearch", cmd);
      Assert.Contains("--openaiurl", cmd);
      Assert.Contains("http://localhost:9000/v1", cmd);
      Assert.Contains("ai/smollm2", cmd);
      // run v1.2.1 exposes neither — they must never be emitted onto `run`.
      Assert.DoesNotContain("--context-size", cmd);
      Assert.DoesNotContain("--backend", cmd);
    }

    [Fact]
    public async Task LoadAsync_DetachFalse_ReturnsInvalidArgumentFailureAndDoesNotRun()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      var result = await driver.LoadAsync(Ctx, ModelReference.Parse("ai/x"),
          new ModelRunOptions { Detach = false }, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, result.ErrorCode);
      Assert.Contains("Detach=false", result.Error);
      Assert.Empty(driver.Commands);
    }

    [Fact]
    public async Task LoadAsync_NullOptions_DefaultsToDetached_NoExtraFlags()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.LoadAsync(Ctx, ModelReference.Parse("ai/x"), null!, TestContext.Current.CancellationToken);

      var cmd = driver.Commands.Single();
      Assert.Contains("model run", cmd);
      Assert.Contains("-d", cmd);
      Assert.DoesNotContain("--debug", cmd);
      Assert.DoesNotContain("--websearch", cmd);
    }

    [Fact]
    public async Task LoadAsync_ConfigureOnlyFields_RoutedThroughConfigureBeforeRun_NothingDropped()
    {
      // ContextSize and RuntimeFlags have no `run` flag — they must be applied via
      // `docker model configure` FIRST (so they are not silently dropped), then the run runs.
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.LoadAsync(Ctx, ModelReference.Parse("ai/x"),
          new ModelRunOptions { ContextSize = 8192, RuntimeFlags = new[] { "--temp", "0.7" }, Debug = true },
          TestContext.Current.CancellationToken);

      Assert.Equal(2, driver.Commands.Count);
      var configure = driver.Commands[0];
      var run = driver.Commands[1];

      // The configure invocation precedes the run and carries the configure-only settings.
      Assert.Contains("model configure", configure);
      Assert.Contains("--context-size 8192", configure);
      Assert.Contains("-- --temp 0.7", configure);

      // The run carries only run-supported flags; configure-only ones never leak onto it.
      Assert.Contains("model run", run);
      Assert.Contains("--debug", run);
      Assert.DoesNotContain("--context-size", run);
      Assert.DoesNotContain("--temp", run);
    }

    [Fact]
    public async Task LoadAsync_PreConfigureFails_RunIsNotAttempted()
    {
      // If the pre-run configure fails, the failure is returned and the run is NOT attempted.
      var driver = new FakeRuntimeDriver
      {
        Responder = args => args.Contains("model configure")
            ? new SimpleCommandResult { Success = false, ExitCode = 1, Error = "configure failed" }
            : Ok()
      };

      var result = await driver.LoadAsync(Ctx, ModelReference.Parse("ai/x"),
          new ModelRunOptions { ContextSize = 4096 }, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Model.ConfigureFailed, result.ErrorCode);
      Assert.DoesNotContain(driver.Commands, c => c.Contains("model run"));
    }

    // ---- A4: log streaming faults surface as a typed ModelRunnerException ----

    [Fact]
    public async Task LogsAsync_StreamFaults_ThrowsModelRunnerExceptionWithLogsCode()
    {
      // The streaming primitive raises a generic DriverException on a failed/aborted stream;
      // LogsAsync must translate it into a model-specific ModelRunnerException (LogsFailed),
      // preserving the message — not leak the generic driver error.
      var driver = new FakeRuntimeDriver
      {
        StreamResponder = _ => FaultingStream()
      };

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
      {
        await foreach (var _ in driver.LogsAsync(Ctx, follow: true, TestContext.Current.CancellationToken))
        {
        }
      });

      Assert.Equal(ErrorCodes.Model.LogsFailed, ex.ErrorCode);
      Assert.Contains("logs stream blew up", ex.Message);
    }

    [Fact]
    public async Task LogsAsync_MidStreamFault_YieldsEarlyLinesThenTypedException()
    {
      // A mid-stream fault (after some lines) must still surface as the typed exception.
      var driver = new FakeRuntimeDriver { StreamResponder = _ => MidStreamFault() };

      var collected = new List<string>();
      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
      {
        await foreach (var line in driver.LogsAsync(Ctx, follow: false, TestContext.Current.CancellationToken))
          collected.Add(line);
      });

      Assert.Equal(new[] { "first" }, collected);
      Assert.Equal(ErrorCodes.Model.LogsFailed, ex.ErrorCode);
    }

    private static IEnumerable<string> FaultingStream()
    {
      // Mirrors the streaming primitive raising a generic DriverException on a failed stream.
      throw new DriverException("Streaming command failed (logs stream blew up).", ErrorCodes.Driver.CommandExecutionFailed);
#pragma warning disable CS0162 // Unreachable — present so this is a deferred-iterator body.
      yield break;
#pragma warning restore CS0162
    }

    private static IEnumerable<string> MidStreamFault()
    {
      yield return "first";
      throw new DriverException("Streaming command failed (exit code 1).", ErrorCodes.Driver.CommandExecutionFailed);
    }
  }
}
