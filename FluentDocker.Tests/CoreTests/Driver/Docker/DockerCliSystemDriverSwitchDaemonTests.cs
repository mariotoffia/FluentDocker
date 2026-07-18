using System;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// DC-12: when Docker Desktop's <c>dockercli</c> is unavailable, the Switch* engine
  /// operations must fail with <see cref="ErrorCodes.Driver.NotAvailable"/> (not
  /// <see cref="ErrorCodes.General.Unknown"/>) and a message stating the Docker Desktop /
  /// Windows requirement.
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerCliSystemDriverSwitchDaemonTests
  {
    /// <summary>
    /// Resolver without a dockercli binary: resolving <c>dockercli</c> throws the same
    /// <see cref="FluentDockerException"/> the real resolver produces on macOS/Linux or a
    /// Windows host without Docker Desktop.
    /// </summary>
    private sealed class NoDockerCliResolver : IBinaryResolver
    {
      private readonly DockerBinary _client =
          new(".", "docker", SudoMechanism.None, null!, DockerBinaryType.DockerClient);

      public DockerBinary[] Binaries => [_client];
      public DockerBinary MainDockerClient => _client;
      public DockerBinary MainDockerCli => null!;

      public DockerBinary Resolve(string binary) =>
          binary == "dockercli"
              ? throw new FluentDockerException(
                  "Could not resolve binary dockercli - is it installed on the local system?")
              : _client;

      public string ResolveBinaryPath(string dockerCommand) => Resolve(dockerCommand).FqPath;
    }

    public static TheoryData<string> SwitchOperations => new("daemon", "linux", "windows");

    [Theory]
    [MemberData(nameof(SwitchOperations))]
    public async Task Switch_WithoutDockerCli_FailsWithDriverNotAvailable(string operation)
    {
      var driver = new DockerCliSystemDriver(new NoDockerCliResolver());
      driver.Initialize(new DriverContext("docker"));

      var context = new DriverContext("docker");
      var result = operation switch
      {
        "daemon" => await driver.SwitchDaemonAsync(context, TestContext.Current.CancellationToken),
        "linux" => await driver.SwitchToLinuxDaemonAsync(context, TestContext.Current.CancellationToken),
        _ => await driver.SwitchToWindowsDaemonAsync(context, TestContext.Current.CancellationToken)
      };

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Driver.NotAvailable, result.ErrorCode);
      Assert.Contains("dockercli", result.Error, StringComparison.OrdinalIgnoreCase);
      Assert.Contains("Docker Desktop", result.Error, StringComparison.Ordinal);
      Assert.Contains("Windows", result.Error, StringComparison.Ordinal);
    }
  }
}
