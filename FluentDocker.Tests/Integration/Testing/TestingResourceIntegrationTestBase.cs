using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluentDocker.Tests.Integration.Testing
{
  public abstract class TestingResourceIntegrationTestBase : IAsyncLifetime
  {
    protected const string DriverId = "docker";
    public const string BusyboxImage = "busybox:latest";
    public const string NginxImage = "nginx:alpine";
    protected const string HelloWorldImage = "hello-world:latest";
    protected FluentDockerKernel Kernel { get; private set; } = null!;
    protected string SessionId { get; } = SessionLabel.NewSessionId();
    protected static DriverContext Context => new(DriverId);
    protected IContainerDriver ContainerDriver => Kernel.SysCtl<IContainerDriver>(DriverId);
    protected INetworkDriver NetworkDriver => Kernel.SysCtl<INetworkDriver>(DriverId);
    protected IVolumeDriver VolumeDriver => Kernel.SysCtl<IVolumeDriver>(DriverId);
    protected IImageDriver ImageDriver => Kernel.SysCtl<IImageDriver>(DriverId);

    public async ValueTask InitializeAsync()
    {
      Kernel = await CreateKernelAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
      GC.SuppressFinalize(this);
      if (Kernel != null)
        await Kernel.DisposeAsync().ConfigureAwait(false);
    }

    public static Task<FluentDockerKernel> CreateKernelAsync()
    {
      return FluentDockerKernel.Create(NullLoggerFactory.Instance)
          .WithDockerCli(DriverId, d => d.AsDefault())
          .BuildAsync();
    }

    protected DockerResourceOptions Options() => new()
    {
      SessionId = SessionId,
      EnableSessionLabels = true,
      InitializationTimeout = TimeSpan.FromSeconds(60),
      TeardownTimeout = TimeSpan.FromSeconds(30)
    };

    protected async Task EnsureImageAsync(string image, CancellationToken cancellationToken)
    {
      var parts = image.Split(':');
      var name = parts[0];
      var tag = parts.Length > 1 ? parts[1] : "latest";
      var existing = await ImageDriver.ListAsync(
          Context, new ImageListFilter { Reference = image }, cancellationToken).ConfigureAwait(false);
      if (existing.Success && existing.Data?.Count > 0)
        return;

      var pull = await ImageDriver.PullAsync(
          Context, name, tag, cancellationToken: cancellationToken)
          .ConfigureAwait(false);
      Assert.True(pull.Success, $"Failed to pull {image}: {pull.Error}");
    }

    public static string UniqueName(string prefix)
    {
      return $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(63, prefix.Length + 33)];
    }

    protected static string ResourcePath(string relativePath)
    {
      var fromOutput = Path.Combine(AppContext.BaseDirectory, relativePath);
      if (File.Exists(fromOutput))
        return fromOutput;

      var dir = new DirectoryInfo(AppContext.BaseDirectory);
      while (dir != null)
      {
        var candidate = Path.Combine(dir.FullName, "FluentDocker.Tests", relativePath);
        if (File.Exists(candidate))
          return candidate;
        dir = dir.Parent;
      }

      throw new FileNotFoundException($"Could not find test resource '{relativePath}'.");
    }

    protected async Task AssertNoContainersByNameAsync(string name, CancellationToken cancellationToken)
    {
      var list = await ContainerDriver.ListAsync(
          Context, new ContainerListFilter { All = true, Name = name }, cancellationToken)
          .ConfigureAwait(false);
      Assert.True(list.Success, list.Error);
      Assert.DoesNotContain(list.Data ?? [], c => string.Equals(
          c.Name?.TrimStart('/'), name, StringComparison.Ordinal));
    }
  }
}
