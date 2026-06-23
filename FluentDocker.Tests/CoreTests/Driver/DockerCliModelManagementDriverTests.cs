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
      var progressEvents = new List<ModelPullProgress>();
      var progress = new Progress<ModelPullProgress>(p => progressEvents.Add(p));

      var driver = new FakeMgmtDriver
      {
        StreamResponder = _ => new[] { "Downloaded 100MB of 200MB", "Downloaded 200MB of 200MB", "Model pulled successfully" },
        Responder = args => args.Contains("inspect") ? Ok(DmrFixtures.Load("inspect.json")) : Ok()
      };

      var result = await driver.PullAsync(Ctx, ModelReference.Parse("ai/smollm2"), progress, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal("smollm2", result.Data.Reference.Name);
      Assert.Contains(driver.Commands, c => c.Contains("model pull"));
      // progress is reported asynchronously; allow the SynchronizationContext-free Progress to flush
      await Task.Delay(20, TestContext.Current.CancellationToken);
      Assert.NotEmpty(progressEvents);
    }
  }
}
