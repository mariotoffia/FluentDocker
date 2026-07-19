using System;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers.Podman;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  public partial class PodServiceTests
  {
    [Fact]
    public async Task RemoveAsync_WhenPodmanCliReportsNoSuchPod_MarksRemoved()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var dir = CreateOutputDirectory("pod-service-podman-cli");
      WriteExecutable(Path.Combine(dir, "podman"), """
          #!/bin/sh
          echo 'Error: no pod with name or ID missing found: no such pod' >&2
          exit 125
          """);
      var podDriver = new PodmanCliPodDriver(new PodmanResolver(dir));
      podDriver.Initialize(new DriverContext("docker"));
      var mockPack = new MockDriverPack();
      mockPack.RegisterCustomDriver<IPodmanPodDriver>(podDriver);
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new PodService(kernel, "docker", "pod-id", "missing");

      try
      {
        await service.RemoveAsync(force: true, TestContext.Current.CancellationToken);

        Assert.Equal(ServiceRunningState.Removed, service.State);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    private static string CreateOutputDirectory(string name)
    {
      var dir = Path.Combine(AppContext.BaseDirectory, ".out", name, Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
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
