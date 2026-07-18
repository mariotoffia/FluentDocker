using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Pins DC-6/POD-13: a buffered-command timeout must carry the PARTIAL stdout/stderr in
  /// the <see cref="DriverException.Context"/> even when the child wrote output and then
  /// hung with its pipes still open (the dominant wedged-daemon case — the cancelled reader
  /// tasks alone would discard the accumulated text).
  /// </summary>
  [Trait("Category", "Unit")]
  public sealed class CliTimeoutDiagnosticsTests
  {
    private const string HangingScript = """
        #!/bin/sh
        printf 'partial-out'
        printf 'partial-err' 1>&2
        sleep 30
        """;

    [Fact]
    public async Task DockerBufferedTimeout_AttachesPartialOutputTails()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake binary; not applicable on Windows");

      var dir = CreateScriptDir("docker");
      var driver = new DockerProbeDriver(new DockerBinariesResolver(
          new BinaryConfiguration { SearchPaths = [dir] }));
      driver.Initialize(new DriverContext("docker"));

      var ex = await Assert.ThrowsAsync<DriverException>(() => driver.ProbeAsync(
          new DriverContext("docker") { RequestTimeout = TimeSpan.FromSeconds(2) },
          "info", CancellationToken.None));

      Assert.Equal(ErrorCodes.General.Timeout, ex.ErrorCode);
      Assert.NotNull(ex.Context);
      Assert.Contains("partial-out", ex.Context.StdOut);
      Assert.Contains("partial-err", ex.Context.StdErr);
    }

    [Fact]
    public async Task PodmanBufferedTimeout_AttachesPartialOutputTails()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake binary; not applicable on Windows");

      var dir = CreateScriptDir("podman");
      var driver = new PodmanProbeDriver(new PodmanBinariesResolver(
          new PodmanBinaryConfiguration { SearchPaths = [dir] }));
      driver.Initialize(new DriverContext("podman"));

      var ex = await Assert.ThrowsAsync<DriverException>(() => driver.ProbeAsync(
          new DriverContext("podman") { RequestTimeout = TimeSpan.FromSeconds(2) },
          "info", CancellationToken.None));

      Assert.Equal(ErrorCodes.General.Timeout, ex.ErrorCode);
      Assert.NotNull(ex.Context);
      Assert.Contains("partial-out", ex.Context.StdOut);
      Assert.Contains("partial-err", ex.Context.StdErr);
    }

    private static string CreateScriptDir(string binaryName)
    {
      var dir = Path.Combine(AppContext.BaseDirectory, ".out", $"cli-timeout-{Guid.NewGuid():N}");
      Directory.CreateDirectory(dir);
      var path = Path.Combine(dir, binaryName);
      File.WriteAllText(path, HangingScript);
      if (!OperatingSystem.IsWindows())
      {
        File.SetUnixFileMode(
            path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
      }

      return dir;
    }

    [Fact]
    public void SudoPreserveEnv_EmitsNamesOnly_AndValidatesIdentifiers()
    {
      // DC-2: caller environment must survive sudo via --preserve-env with NAMES only
      // (values stay in the process environment), and hostile names must be rejected
      // before they can smuggle arguments onto the sudo command line.
      var build = typeof(DockerCliDriverBase).GetMethod(
          "BuildSudoCommand",
          System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
          [typeof(string), typeof(string), typeof(SudoMechanism), typeof(string), typeof(System.Collections.Generic.IReadOnlyCollection<string>)]);
      Assert.NotNull(build);

      var (fileName, arguments, _) =
          ((string, string, string))build!.Invoke(null,
              ["/usr/bin/docker", "info", SudoMechanism.NoPassword, null, new[] { "COMPOSE_PROJECT_NAME", "MY_VAR" }])!;
      Assert.Equal("sudo", fileName);
      Assert.Contains("--preserve-env=COMPOSE_PROJECT_NAME,MY_VAR", arguments);
      Assert.DoesNotContain("secret", arguments);

      var (noEnvFile, noEnvArgs, _) =
          ((string, string, string))build.Invoke(null,
              ["/usr/bin/docker", "info", SudoMechanism.None, null, new[] { "MY_VAR" }])!;
      Assert.Equal("/usr/bin/docker", noEnvFile);
      Assert.DoesNotContain("--preserve-env", noEnvArgs);

      var validate = typeof(DockerCliDriverBase).GetMethod(
          "ValidatedPreserveEnvNames",
          System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
      Assert.NotNull(validate);
      var hostile = new System.Collections.Generic.Dictionary<string, string> { ["BAD NAME --preserve-env=X"] = "v" };
      var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
          validate!.Invoke(null, [hostile, SudoMechanism.NoPassword]));
      Assert.IsType<DriverException>(ex.InnerException);
    }

    private sealed class DockerProbeDriver(IBinaryResolver resolver) : DockerCliDriverBase(resolver)
    {
      public Task<SimpleCommandResult> ProbeAsync(DriverContext context, string args, CancellationToken ct) =>
          ExecuteCommandAsync(context, args, ct);
    }

    private sealed class PodmanProbeDriver(IPodmanBinaryResolver resolver) : PodmanCliDriverBase(resolver)
    {
      public Task<SimpleCommandResult> ProbeAsync(DriverContext context, string args, CancellationToken ct) =>
          ExecuteCommandAsync(context, args, ct);
    }
  }
}
