using FluentDocker.Drivers.Podman.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Unit tests for the <c>podman exec</c> infrastructure-vs-command failure heuristic
  /// (FIX-5). Exercised through the public static
  /// <see cref="PodmanCliContainerDriver.IsExecInfrastructureFailure"/> seam because the
  /// driver itself spawns a real <c>podman</c> process and cannot be unit-tested directly.
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodmanCliExecClassificationTests
  {
    [Fact]
    public void ProcessCouldNotStartSentinel_IsInfraFailure()
    {
      // ExecuteProcessAsync returns ExitCode -1 when the process could not start at all.
      Assert.True(PodmanCliContainerDriver.IsExecInfrastructureFailure(-1, "", "spawn error"));
    }

    [Fact]
    public void PodmanExecFailureExitCode125_IsInfraFailure()
    {
      // Podman returns 125 when the exec operation itself fails (no such container, not
      // running, etc.) — podman's own failure code, not the in-container command's.
      Assert.True(PodmanCliContainerDriver.IsExecInfrastructureFailure(125, "", "Error: no container with name"));
    }

    [Fact]
    public void ExitCode125_WithStdout_IsNotInfraFailure()
    {
      // An in-container command that produced output and happened to exit 125 is the command's
      // own result, not a podman exec failure. The stdout guard must win over the 125 check so
      // such a command is preserved as a successful exec carrying its exit code.
      Assert.False(PodmanCliContainerDriver.IsExecInfrastructureFailure(
          125, "real command output", "some warning"));
    }

    [Theory]
    [InlineData("Error: no such container abc123")]
    [InlineData("Error: can only create exec sessions on running containers: container is not running")]
    [InlineData("Error: unable to exec into container abc123")]
    [InlineData("Error: cannot connect to the Podman socket")]
    public void PodmanSignatureWithEmptyStdout_IsInfraFailure(string stderr)
    {
      Assert.True(PodmanCliContainerDriver.IsExecInfrastructureFailure(1, "", stderr));
    }

    [Theory]
    [InlineData(3, "", "supervisord: web is not running")]
    [InlineData(3, "", "systemctl: Unit foo.service is not running")]
    public void BareNotRunningPhraseFromInContainerTool_IsNotInfraFailure(int exitCode, string stdOut, string stdErr)
    {
      // "is not running" also appears in legitimate in-container tool output; without an
      // "Error: " podman marker these must NOT be classified as infra failures.
      Assert.False(PodmanCliContainerDriver.IsExecInfrastructureFailure(exitCode, stdOut, stdErr));
    }

    [Theory]
    [InlineData("Error: cannot connect to redis")]
    [InlineData("Error: my-service is not running")]
    public void AppErrorWithGenericPhrase_IsNotInfraFailure(string stderr)
    {
      // An in-container app can itself print "Error: ..." with a generic phrase ("cannot
      // connect", "is not running"). Only podman's specific phrasing ("cannot connect to the
      // podman", "container is not running") signals an infra failure, so these app errors
      // (non-zero exit, empty stdout) must be preserved as command results, not infra failures.
      Assert.False(PodmanCliContainerDriver.IsExecInfrastructureFailure(1, "", stderr));
    }

    [Fact]
    public void InContainerNonZeroExit_WithStdout_IsNotInfraFailure()
    {
      // A real command that ran and exited non-zero (produced output) must be preserved as a
      // successful exec carrying its exit code, not reported as an infra failure.
      Assert.False(PodmanCliContainerDriver.IsExecInfrastructureFailure(
          2, "partial output", "grep: pattern not found"));
    }

    [Fact]
    public void InContainerNonZeroExit_EmptyStdout_NonPodmanStderr_IsNotInfraFailure()
    {
      // e.g. `cat /missing` -> exit 1, stderr "No such file", empty stdout. Not podman-level.
      Assert.False(PodmanCliContainerDriver.IsExecInfrastructureFailure(
          1, "", "cat: /missing: No such file or directory"));
    }

    [Fact]
    public void ShellCommandNotFound127_IsNotInfraFailure()
    {
      // 127 (command not found) is emitted by the in-container shell, not podman.
      Assert.False(PodmanCliContainerDriver.IsExecInfrastructureFailure(
          127, "", "sh: nosuchcmd: not found"));
    }

    [Fact]
    public void SuccessfulExit_IsNotInfraFailure()
    {
      Assert.False(PodmanCliContainerDriver.IsExecInfrastructureFailure(0, "", ""));
    }

    [Fact]
    public void NullStreams_DoNotThrow()
    {
      Assert.False(PodmanCliContainerDriver.IsExecInfrastructureFailure(0, null!, null!));
      Assert.True(PodmanCliContainerDriver.IsExecInfrastructureFailure(-1, null!, null!));
      Assert.True(PodmanCliContainerDriver.IsExecInfrastructureFailure(125, null!, null!));
    }
  }
}
