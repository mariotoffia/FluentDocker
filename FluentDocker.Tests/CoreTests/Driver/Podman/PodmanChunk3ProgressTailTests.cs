using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public class PodmanChunk3ProgressTailTests
  {
    [Fact]
    public async Task BuildAsync_ReportsAllProgressButKeepsOutputTailBounded()
    {
      RequirePosixShellFixture();
      var line = new string('x', 1200);
      var driver = CreateImageDriver($"""
          #!/bin/sh
          iid=''
          while [ "$#" -gt 0 ]; do
            if [ "$1" = '--iidfile' ]; then shift; iid="$1"; fi
            shift
          done
          [ -n "$iid" ] && printf '%s' 'sha256:built' > "$iid"
          i=0
          while [ "$i" -lt 300 ]; do
            echo '{line}'
            i=$((i + 1))
          done
          exit 0
          """);
      var progress = new RecordingProgress<ImageBuildProgress>();

      var result = await driver.BuildAsync(
          new DriverContext("podman"),
          new ImageBuildConfig { BuildContext = "." },
          progress,
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(300, progress.Items.Count);
      Assert.True(result.Data.Output.Count < progress.Items.Count);
    }

    private static PodmanCliImageDriver CreateImageDriver(string script)
    {
      var driver = new PodmanCliImageDriver(new PodmanResolver(CreatePodmanDirectory(script)));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static string CreatePodmanDirectory(string script)
    {
      var dir = Path.Combine(AppContext.BaseDirectory, ".out", "podman-chunk3-progress-tail", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      WriteExecutable(Path.Combine(dir, "podman"), script);
      return dir;
    }

    private static void WriteExecutable(string path, string script)
    {
      File.WriteAllText(path, script.Replace("\r\n", "\n"));
      if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void RequirePosixShellFixture()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
      public List<T> Items { get; } = [];
      public void Report(T value) => Items.Add(value);
    }

    private sealed class PodmanResolver(string directory) : IPodmanBinaryResolver
    {
      private readonly PodmanBinary _binary = new(directory, "podman", SudoMechanism.None, null!, PodmanBinaryType.PodmanClient);
      public PodmanBinary[] Binaries => [_binary];
      public PodmanBinary MainPodmanClient => _binary;
      public PodmanBinary PodmanRemote => _binary;
      public PodmanBinary Resolve(string binary) => _binary;
      public string ResolveBinaryPath(string podmanCommand) => _binary.FqPath;
    }
  }
}
