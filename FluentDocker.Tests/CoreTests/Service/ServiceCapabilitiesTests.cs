using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public class ServiceCapabilitiesTests
  {
    private static async Task<FluentDockerKernel> CreateKernelAsync()
    {
      var mockPack = new MockDriverPack();
      return await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
    }

    [Fact]
    public async Task ContainerService_SupportsAllOperations()
    {
      var kernel = await CreateKernelAsync();
      try
      {
        var service = new ContainerService(kernel, "docker", "c1", "nginx", "test");
        var caps = (IServiceCapabilities)service;
        Assert.True(caps.CanStart);
        Assert.True(caps.CanStop);
        Assert.True(caps.CanPause);
        Assert.True(caps.CanRemove);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task HostService_DoesNotSupportPause()
    {
      var kernel = await CreateKernelAsync();
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var caps = (IServiceCapabilities)service;
        Assert.True(caps.CanStart);
        Assert.True(caps.CanStop);
        Assert.False(caps.CanPause);
        Assert.True(caps.CanRemove);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task ImageService_DoesNotSupportStartStopPause()
    {
      var kernel = await CreateKernelAsync();
      try
      {
        var service = new ImageService(kernel, "docker", "sha256:abc", "nginx:latest", "test");
        var caps = (IServiceCapabilities)service;
        Assert.False(caps.CanStart);
        Assert.False(caps.CanStop);
        Assert.False(caps.CanPause);
        Assert.True(caps.CanRemove);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task NetworkService_OnlySupportsRemove()
    {
      var kernel = await CreateKernelAsync();
      try
      {
        var service = new NetworkService(kernel, "docker", "net1", "test-net");
        var caps = (IServiceCapabilities)service;
        Assert.False(caps.CanStart);
        Assert.False(caps.CanStop);
        Assert.False(caps.CanPause);
        Assert.True(caps.CanRemove);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task VolumeService_OnlySupportsRemove()
    {
      var kernel = await CreateKernelAsync();
      try
      {
        var service = new VolumeService(kernel, "docker", "vol1", "test-vol");
        var caps = (IServiceCapabilities)service;
        Assert.False(caps.CanStart);
        Assert.False(caps.CanStop);
        Assert.False(caps.CanPause);
        Assert.True(caps.CanRemove);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task PodService_DoesNotSupportPause()
    {
      var kernel = await CreateKernelAsync();
      try
      {
        var service = new PodService(kernel, "docker", "pod1", "test-pod");
        var caps = (IServiceCapabilities)service;
        Assert.True(caps.CanStart);
        Assert.True(caps.CanStop);
        Assert.False(caps.CanPause);
        Assert.True(caps.CanRemove);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task AllServices_ImplementIServiceCapabilities()
    {
      var kernel = await CreateKernelAsync();
      try
      {
        Assert.IsAssignableFrom<IServiceCapabilities>(
            new ContainerService(kernel, "docker", "c1", "nginx", "test"));
        Assert.IsAssignableFrom<IServiceCapabilities>(
            new HostService(kernel, "docker", "test-host"));
        Assert.IsAssignableFrom<IServiceCapabilities>(
            new ImageService(kernel, "docker", "sha256:abc", "nginx:latest", "test"));
        Assert.IsAssignableFrom<IServiceCapabilities>(
            new NetworkService(kernel, "docker", "net1", "test-net"));
        Assert.IsAssignableFrom<IServiceCapabilities>(
            new VolumeService(kernel, "docker", "vol1", "test-vol"));
        Assert.IsAssignableFrom<IServiceCapabilities>(
            new PodService(kernel, "docker", "pod1", "test-pod"));
        Assert.IsAssignableFrom<IServiceCapabilities>(
            new ComposeService(kernel, "docker", ["compose.yml"], "test-project"));
      }
      finally { kernel.Dispose(); }
    }
  }
}
