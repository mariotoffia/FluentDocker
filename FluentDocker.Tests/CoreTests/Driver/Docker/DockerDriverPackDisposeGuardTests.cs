using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public class DockerDriverPackDisposeGuardTests : IDisposable
  {
    private readonly string _scratch =
        Path.Combine(AppContext.BaseDirectory, ".out", $"docker-pack-{Guid.NewGuid():N}");

    public void Dispose()
    {
      if (Directory.Exists(_scratch))
        Directory.Delete(_scratch, recursive: true);
      GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task DockerApiDriverPack_AfterDispose_RejectsResolveAndReinitialize()
    {
      var pack = new DockerApiDriverPack();
      await pack.InitializeAsync(
          new DriverContext("api"), TestContext.Current.CancellationToken);

      await pack.DisposeAsync();

      Assert.Throws<ObjectDisposedException>(() =>
          pack.TryResolve(typeof(IContainerDriver), out _));
      await Assert.ThrowsAsync<ObjectDisposedException>(() =>
          pack.InitializeAsync(
              new DriverContext("api"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DockerCliDriverPack_AfterDispose_RejectsLazyInferenceResolve()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX executable bits are required for the fake docker client");

      var docker = CreateFakeDockerClient();
      var pack = new DockerCliDriverPack();
      await pack.InitializeAsync(
          new DriverContext("docker")
          {
            SearchPaths = [_scratch]
          },
          TestContext.Current.CancellationToken);
      Assert.True(pack.TryResolve(typeof(IModelInferenceDriver), out _));

      await pack.DisposeAsync();

      Assert.Throws<ObjectDisposedException>(() =>
          pack.TryResolve(typeof(IModelInferenceDriver), out _));
      await Assert.ThrowsAsync<ObjectDisposedException>(() =>
          pack.InitializeAsync(
              new DriverContext("docker")
              {
                SearchPaths = [Path.GetDirectoryName(docker)!]
              },
              TestContext.Current.CancellationToken));
    }

    private string CreateFakeDockerClient()
    {
      Directory.CreateDirectory(_scratch);
      var docker = Path.Combine(_scratch, "docker");
      File.WriteAllText(docker, "#!/bin/sh\nif [ \"$1\" = \"compose\" ]; then echo 'Docker Compose version v2.0.0'; fi\n");
      if (!OperatingSystem.IsWindows())
      {
        File.SetUnixFileMode(
            docker,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
      }
      return docker;
    }
  }
}
