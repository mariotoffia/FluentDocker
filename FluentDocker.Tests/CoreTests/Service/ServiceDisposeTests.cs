using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

#pragma warning disable CS0618 // IService obsolete — intentional test usage

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public class ServiceDisposeTests
  {
    private static async Task<FluentDockerKernel> CreateKernelAsync()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerStop();
      mockPack.SetupContainerRemove();
      mockPack.NetworkDriver
          .Setup(d => d.RemoveAsync(It.IsAny<DriverContext>(), It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      mockPack.VolumeDriver
          .Setup(d => d.RemoveAsync(It.IsAny<DriverContext>(), It.IsAny<string>(),
              It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      return await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
    }

    [Fact]
    public async Task ContainerService_Dispose_CalledTwice_DoesNotThrow()
    {
      var kernel = await CreateKernelAsync();
      try
      {
        var svc = new ContainerService(kernel, "docker", "c1", "nginx", "t",
            stopOnDispose: false, deleteOnDispose: false);
        svc.Dispose();
        svc.Dispose();
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task ContainerService_DisposeAsync_CalledTwice_DoesNotThrow()
    {
      var kernel = await CreateKernelAsync();
      try
      {
        var svc = new ContainerService(kernel, "docker", "c1", "nginx", "t",
            stopOnDispose: false, deleteOnDispose: false);
        await svc.DisposeAsync();
        await svc.DisposeAsync();
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task ContainerService_Dispose_AfterDisposeAsync_DoesNotThrow()
    {
      var kernel = await CreateKernelAsync();
      try
      {
        var svc = new ContainerService(kernel, "docker", "c1", "nginx", "t",
            stopOnDispose: false, deleteOnDispose: false);
        await svc.DisposeAsync();
        svc.Dispose();
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task HostService_Dispose_CalledTwice_DoesNotThrow()
    {
      var kernel = await CreateKernelAsync();
      try
      {
        var svc = new HostService(kernel, "docker", "test-host");
        svc.Dispose();
        svc.Dispose();
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task ImageService_Dispose_CalledTwice_DoesNotThrow()
    {
      var kernel = await CreateKernelAsync();
      try
      {
        var svc = new ImageService(kernel, "docker", "sha256:abc", "nginx:latest", "t");
        svc.Dispose();
        svc.Dispose();
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task NetworkService_Dispose_CalledTwice_DoesNotThrow()
    {
      var kernel = await CreateKernelAsync();
      try
      {
        var svc = new NetworkService(kernel, "docker", "net1", "test-net", removeOnDispose: false);
        svc.Dispose();
        svc.Dispose();
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task VolumeService_Dispose_CalledTwice_DoesNotThrow()
    {
      var kernel = await CreateKernelAsync();
      try
      {
        var svc = new VolumeService(kernel, "docker", "vol1", "test-vol", removeOnDispose: false);
        svc.Dispose();
        svc.Dispose();
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task PodService_Dispose_CalledTwice_DoesNotThrow()
    {
      var kernel = await CreateKernelAsync();
      try
      {
        var svc = new PodService(kernel, "docker", "pod1", "test-pod", removeOnDispose: false);
        svc.Dispose();
        svc.Dispose();
      }
      finally { kernel.Dispose(); }
    }
  }
}
