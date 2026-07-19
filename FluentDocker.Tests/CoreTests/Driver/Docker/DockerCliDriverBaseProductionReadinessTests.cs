using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Production-readiness coverage for shared <see cref="DockerCliDriverBase"/> behaviour: quoting
  /// the docker binary path under sudo, capping unbounded command output while keeping the tail and
  /// exit code, and emitting TLS flags from a certificate path even without an explicit host.
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public sealed class DockerCliDriverBaseProductionReadinessTests : DockerCliFakeDockerTestBase
  {
    [Fact]
    public async Task SudoCommand_QuotesDockerBinaryPath()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var dir = TestOutputDirectory();
      var sudoDir = Path.Combine(dir, $"sudo-{Guid.NewGuid():N}");
      var dockerDir = Path.Combine(dir, $"docker bin {Guid.NewGuid():N}");
      Directory.CreateDirectory(sudoDir);
      Directory.CreateDirectory(dockerDir);
      var record = Path.Combine(dir, $"sudo-args-{Guid.NewGuid():N}.txt");
      WriteExecutable(Path.Combine(sudoDir, "sudo"), $"""
#!/bin/sh
printf '%s\n' "$@" > '{record}'
exit 0
""");
      var dockerPath = Path.Combine(dockerDir, "docker");
      WriteExecutable(dockerPath, "#!/bin/sh\nexit 0\n");
      var oldPath = Environment.GetEnvironmentVariable("PATH");
      try
      {
        Environment.SetEnvironmentVariable("PATH", $"{sudoDir}:{oldPath}");
        var driver = new DockerCliContainerDriver(new SudoResolver(dockerPath));
        driver.Initialize(new DriverContext("docker"));

        var result = await driver.StartAsync(
            new DriverContext("docker") { Sudo = SudoMechanism.NoPassword },
            "ctr",
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Equal(["-n", "--", dockerPath, "start", "ctr"], await ReadArgsAsync(record));
      }
      finally
      {
        Environment.SetEnvironmentVariable("PATH", oldPath);
      }
    }

    [Fact]
    public async Task UnboundedCommand_OutputBeyondCap_KeepsTailMarkerAndExitCode()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell semantics; not applicable on Windows");

      var driver = new ShellDriver();
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.RunUnbounded(
          "-c \"yes head | head -c 5000000; printf TAIL; exit 7\"",
          TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(7, result.ExitCode);
      Assert.Contains("[FluentDocker: output truncated", result.Output);
      Assert.EndsWith("TAIL", result.Output);
      Assert.True(result.Output.Length < 512 * 1024);
    }

    [Fact]
    public void BuildGlobalArgs_CertificatePathWithoutHost_StillEmitsTlsFlags()
    {
      var result = DockerCliDriverBase.BuildGlobalArgs(new DriverContext("docker")
      {
        CertificatePath = "/certs",
        VerifyTls = true
      });

      Assert.DoesNotContain("-H", result);
      Assert.Contains("--tlsverify", result);
      Assert.Contains("ca.pem", result);
    }

    private static void WriteExecutable(string path, string script)
    {
      File.WriteAllText(path, script.Replace("\r\n", "\n"));
      if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private sealed class ShellDriver : DockerCliDriverBase
    {
      public ShellDriver() : base(new FakeResolver("/bin/sh"))
      {
      }

      public Task<SimpleCommandResult> RunUnbounded(string args, CancellationToken cancellationToken) =>
          ExecuteUnboundedCommandAsync(args, cancellationToken);
    }

    private sealed class SudoResolver(string dockerPath) : IBinaryResolver
    {
      private readonly DockerBinary _binary = new(
          Path.GetDirectoryName(dockerPath) ?? ".",
          Path.GetFileName(dockerPath),
          SudoMechanism.NoPassword,
          null!,
          DockerBinaryType.DockerClient);

      public DockerBinary[] Binaries => [_binary];
      public DockerBinary MainDockerClient => _binary;
      public DockerBinary MainDockerCli => _binary;
      public DockerBinary Resolve(string binary) => _binary;
      public string ResolveBinaryPath(string dockerCommand) => _binary.FqPath;
    }
  }
}
