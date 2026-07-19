using System;
using FluentDocker.Drivers.Docker.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Unit tests for the <c>docker exec</c> infrastructure-vs-command failure heuristic
  /// (FIX-5). Exercised through the public static
  /// <see cref="DockerCliContainerDriver.IsExecInfrastructureFailure"/> seam because the
  /// driver itself spawns a real <c>docker</c> process and cannot be unit-tested directly.
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerCliExecClassificationTests
  {
    [Fact]
    public void ProcessCouldNotStartSentinel_IsInfraFailure()
    {
      // ExecuteProcessAsync returns ExitCode -1 when the process could not start at all.
      Assert.True(DockerCliContainerDriver.IsExecInfrastructureFailure(-1, "", "spawn error"));
    }

    [Theory]
    [InlineData("Error response from daemon: No such container: abc123")]
    [InlineData("Error response from daemon: Container abc123 is not running")]
    [InlineData("Cannot connect to the Docker daemon at unix:///var/run/docker.sock")]
    public void DaemonSignatureWithEmptyStdout_IsInfraFailure(string stderr)
    {
      Assert.True(DockerCliContainerDriver.IsExecInfrastructureFailure(1, "", stderr));
    }

    [Theory]
    [InlineData(3, "", "supervisord: web is not running")]
    [InlineData(3, "", "systemctl: Unit foo.service is not running")]
    public void BareNotRunningPhraseFromInContainerTool_IsNotInfraFailure(int exitCode, string stdOut, string stdErr)
    {
      // "is not running" / "No such container" also appear in legitimate in-container tool
      // output; without a docker-CLI/daemon marker these must NOT be classified as infra.
      Assert.False(DockerCliContainerDriver.IsExecInfrastructureFailure(exitCode, stdOut, stdErr));
    }

    [Fact]
    public void InContainerNonZeroExit_WithStdout_IsNotInfraFailure()
    {
      // A real command that ran and exited non-zero (produced output) must be preserved
      // as a successful exec carrying its exit code, not reported as an infra failure.
      Assert.False(DockerCliContainerDriver.IsExecInfrastructureFailure(
          2, "partial output", "grep: pattern not found"));
    }

    [Fact]
    public void InContainerNonZeroExit_EmptyStdout_NonDaemonStderr_IsNotInfraFailure()
    {
      // e.g. `cat /missing` -> exit 1, stderr "No such file", empty stdout. Not daemon-level.
      Assert.False(DockerCliContainerDriver.IsExecInfrastructureFailure(
          1, "", "cat: /missing: No such file or directory"));
    }

    [Fact]
    public void SuccessfulExit_IsNotInfraFailure()
    {
      Assert.False(DockerCliContainerDriver.IsExecInfrastructureFailure(0, "", ""));
    }

    [Fact]
    public void NullStreams_DoNotThrow()
    {
      Assert.False(DockerCliContainerDriver.IsExecInfrastructureFailure(0, null!, null!));
      Assert.True(DockerCliContainerDriver.IsExecInfrastructureFailure(-1, null!, null!));
    }
  }
}
