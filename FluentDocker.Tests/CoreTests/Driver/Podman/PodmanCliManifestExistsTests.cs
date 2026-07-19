using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Unit tests for the manifest existence classifier (P2). <c>ExistsAsync</c> must only report
  /// <c>Ok(false)</c> for a genuine "manifest not found" signal; a machine-down / auth / CLI
  /// failure must surface as a FAILED response instead of masquerading as "missing". The whole
  /// decision routes through <see cref="PodmanCliManifestDriver.IsManifestNotFound"/>:
  /// <c>Success ⇒ Ok(true)</c>; <c>IsManifestNotFound ⇒ Ok(false)</c>; otherwise <c>Fail</c>.
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodmanCliManifestExistsTests
  {
    [Fact]
    public void IsManifestNotFound_Exit1EmptyStderr_ReturnsTrue()
    {
      // podman's `exists` family returns exit 1 with empty stderr for a plain miss.
      var result = new SimpleCommandResult { Success = false, ExitCode = 1, Error = "" };

      Assert.True(PodmanCliManifestDriver.IsManifestNotFound(result));
    }

    [Theory]
    [InlineData("manifest unknown")]
    [InlineData("Error: mylist: manifest unknown")]
    [InlineData("image not found")]
    public void IsManifestNotFound_NotFoundStderr_ReturnsTrue(string stderr)
    {
      var result = new SimpleCommandResult { Success = false, ExitCode = 125, Error = stderr };

      Assert.True(PodmanCliManifestDriver.IsManifestNotFound(result));
    }

    [Theory]
    [InlineData(125, "Cannot connect to Podman. Please verify your connection to the Linux system")]
    [InlineData(125, "unauthorized: authentication required")]
    [InlineData(1, "unauthorized: authentication required")]
    [InlineData(255, "connection refused")]
    // Generic "no such" / "does not exist" are deliberately NOT trusted as not-found: they occur in
    // real outages, so the narrowed classifier surfaces them as failures rather than absence.
    [InlineData(125, "no such manifest list")]
    [InlineData(125, "localhost/mylist: does not exist")]
    public void IsManifestNotFound_OutageOrAuthStderr_ReturnsFalse(int exitCode, string stderr)
    {
      // An outage / auth / CLI failure must NOT be reported as "not found".
      var result = new SimpleCommandResult { Success = false, ExitCode = exitCode, Error = stderr };

      Assert.False(PodmanCliManifestDriver.IsManifestNotFound(result));
    }

    [Fact]
    public void IsManifestNotFound_NullResult_ReturnsFalse()
    {
      Assert.False(PodmanCliManifestDriver.IsManifestNotFound(null!));
    }

    [Fact]
    public void ExistsAsync_StoppedMachineSocketOutage_SurfacesFailureNotAbsence()
    {
      // THE P2 regression guard. podman's stopped-machine / missing-socket stderr contains
      // "no such file or directory". The pre-fix classifier matched the bare "no such" substring and
      // returned true, so ExistsAsync returned Ok(false) — an outage masquerading as "manifest
      // missing". The narrowed classifier returns false, so ExistsAsync routes to a FAILED response
      // (IsSuccess == false). ExistsAsync itself spawns a real podman process (no seam without
      // InternalsVisibleTo), so this asserts the routing decision at its unit-testable seam.
      var result = new SimpleCommandResult
      {
        Success = false,
        ExitCode = 125,
        Error = "Error: unable to connect to Podman socket: dial unix /run/podman/podman.sock: " +
                "connect: no such file or directory"
      };

      Assert.False(PodmanCliManifestDriver.IsManifestNotFound(result));
    }
  }
}
