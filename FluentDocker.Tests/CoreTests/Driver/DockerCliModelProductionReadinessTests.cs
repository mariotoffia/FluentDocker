using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  [Trait("Category", "Unit")]
  public class DockerCliModelProductionReadinessTests
  {
    private static readonly DriverContext Ctx = new("docker");
    private const string PluginMissing = "docker: 'model' is not a docker command.";

    [Fact]
    public async Task ListAsync_PluginMissingStderr_ReturnsPluginMissingWithInstallHint()
    {
      var driver = new FakeManagementDriver
      {
        Responder = _ => new SimpleCommandResult { Success = false, Error = PluginMissing, ExitCode = 1 }
      };

      var result = await driver.ListAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Model.PluginMissing, result.ErrorCode);
      Assert.Contains("docker-model-plugin", result.Error, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListAsync_UnrelatedFailure_RemainsListFailed()
    {
      var driver = new FakeManagementDriver
      {
        Responder = _ => new SimpleCommandResult { Success = false, Error = "permission denied", ExitCode = 1 }
      };

      var result = await driver.ListAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Model.ListFailed, result.ErrorCode);
    }

    [Fact]
    public async Task ListAsync_PluginMissingStdoutOnly_ReturnsPluginMissingCodeAndMessage()
    {
      var driver = new FakeManagementDriver
      {
        Responder = _ => new SimpleCommandResult { Success = false, Output = PluginMissing, ExitCode = 1 }
      };

      var result = await driver.ListAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Model.PluginMissing, result.ErrorCode);
      Assert.Contains("docker-model-plugin", result.Error, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StatusAsync_PluginMissingStderr_ReturnsPluginMissingWithInstallHint()
    {
      var driver = new FakeRuntimeDriver
      {
        Responder = _ => new SimpleCommandResult { Success = false, Error = PluginMissing, ExitCode = 1 }
      };

      var result = await driver.StatusAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Model.PluginMissing, result.ErrorCode);
      Assert.Contains("Docker Model plugin", result.Error, System.StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeManagementDriver : DockerCliModelManagementDriver
    {
      public System.Func<string, SimpleCommandResult> Responder { get; set; } =
          _ => new SimpleCommandResult { Success = true, Output = string.Empty, ExitCode = 0 };

      public FakeManagementDriver() : base(null!)
      {
      }

      protected override Task<SimpleCommandResult> RunAsync(
          DriverContext context, string arguments, CancellationToken cancellationToken) =>
          Task.FromResult(Responder(arguments));
    }

    private sealed class FakeRuntimeDriver : DockerCliModelRuntimeDriver
    {
      public System.Func<string, SimpleCommandResult> Responder { get; set; } =
          _ => new SimpleCommandResult { Success = true, Output = string.Empty, ExitCode = 0 };

      public FakeRuntimeDriver() : base(null!)
      {
      }

      protected override Task<SimpleCommandResult> RunAsync(
          DriverContext context, string arguments, CancellationToken cancellationToken) =>
          Task.FromResult(Responder(arguments));
    }
  }
}
