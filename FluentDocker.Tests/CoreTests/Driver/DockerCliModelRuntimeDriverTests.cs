using System;
using System.Collections.Generic;
using System.Linq;
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
  /// Unit tests for <see cref="DockerCliModelRuntimeDriver"/>, with emphasis on
  /// the <c>configure</c> command assembly (context-size / reset / backend /
  /// <c>--hf_overrides</c> / <c>--</c> runtime-flag passthrough).
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerCliModelRuntimeDriverTests
  {
    private static DriverContext Ctx => new("docker");

    private sealed class FakeRuntimeDriver : DockerCliModelRuntimeDriver
    {
      public List<string> Commands { get; } = [];
      public Func<string, SimpleCommandResult> Responder { get; set; }
      public Func<string, IEnumerable<string>> StreamResponder { get; set; }

      public FakeRuntimeDriver() : base(null)
      {
      }

      protected override Task<SimpleCommandResult> RunAsync(string arguments, CancellationToken cancellationToken)
      {
        Commands.Add(arguments);
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
    public async Task LoadAsync_DetachedWithMemoryCheckSkip()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.LoadAsync(Ctx, ModelReference.Parse("ai/smollm2"),
          new ModelRunOptions { IgnoreRuntimeMemoryCheck = true }, TestContext.Current.CancellationToken);

      var cmd = driver.Commands.Single();
      Assert.Contains("model run", cmd);
      Assert.Contains("-d", cmd);
      Assert.Contains("--ignore-runtime-memory-check", cmd);
      Assert.Contains("ai/smollm2", cmd);
    }

    [Fact]
    public async Task UnloadAsync_All()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.UnloadAsync(Ctx, ModelReference.Parse("ai/smollm2"), all: true, TestContext.Current.CancellationToken);
      Assert.Contains("--all", driver.Commands.Single());
    }

    [Fact]
    public async Task UnloadAsync_Single()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.UnloadAsync(Ctx, ModelReference.Parse("ai/smollm2"), all: false, TestContext.Current.CancellationToken);
      var cmd = driver.Commands.Single();
      Assert.Contains("model unload", cmd);
      Assert.Contains("ai/smollm2", cmd);
      Assert.DoesNotContain("--all", cmd);
    }

    [Fact]
    public async Task ConfigureAsync_ContextSize()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.ConfigureAsync(Ctx, ModelReference.Parse("ai/x"),
          new ModelConfigureOptions { ContextSize = 8192 }, TestContext.Current.CancellationToken);

      Assert.Contains("--context-size 8192", driver.Commands.Single());
    }

    [Fact]
    public async Task ConfigureAsync_ResetContextSize_EmitsMinusOne()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.ConfigureAsync(Ctx, ModelReference.Parse("ai/x"),
          new ModelConfigureOptions { ResetContextSize = true }, TestContext.Current.CancellationToken);

      Assert.Contains("--context-size -1", driver.Commands.Single());
    }

    [Fact]
    public async Task ConfigureAsync_Backend()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.ConfigureAsync(Ctx, ModelReference.Parse("ai/x"),
          new ModelConfigureOptions { Backend = ModelBackend.Vllm }, TestContext.Current.CancellationToken);

      Assert.Contains("--backend vllm", driver.Commands.Single());
    }

    [Fact]
    public async Task ConfigureAsync_HfOverrides()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.ConfigureAsync(Ctx, ModelReference.Parse("ai/x"),
          new ModelConfigureOptions { HfOverridesJson = "{\"max_model_len\":8192}" }, TestContext.Current.CancellationToken);

      Assert.Contains("--hf_overrides", driver.Commands.Single());
      Assert.Contains("max_model_len", driver.Commands.Single());
    }

    [Fact]
    public async Task ConfigureAsync_RuntimeFlags_AfterDoubleDash_AndRefBefore()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.ConfigureAsync(Ctx, ModelReference.Parse("ai/x"),
          new ModelConfigureOptions { RuntimeFlags = new[] { "--temp", "0.7", "--top-p", "0.9" } }, TestContext.Current.CancellationToken);

      var cmd = driver.Commands.Single();
      var dashIndex = cmd.IndexOf(" -- ", StringComparison.Ordinal);
      var refIndex = cmd.IndexOf("ai/x", StringComparison.Ordinal);
      Assert.True(dashIndex > 0, "expected a ' -- ' separator");
      Assert.True(refIndex >= 0 && refIndex < dashIndex, "model ref must precede the -- separator");
      Assert.Contains("-- --temp 0.7 --top-p 0.9", cmd);
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
