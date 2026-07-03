using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public class ServiceContractRemediationTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public void DriverException_TimeoutCode_IsTransient()
    {
      var error = new DriverException("timeout", ErrorCodes.General.Timeout, new ErrorContext("test"));

      Assert.True(error.IsTransient);
    }

    [Fact]
    public void DriverException_NotFoundCode_IsNotTransient()
    {
      var error = new DriverException("missing", ErrorCodes.Container.NotFound, new ErrorContext("test"));

      Assert.False(error.IsTransient);
    }

    [Fact]
    public async Task AddHookWithGeneratedName_ReturnsRemovableName()
    {
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");
      var fired = false;
      var name = service.AddHookWithGeneratedName(ServiceRunningState.Running, _ =>
      {
        fired = true;
        return Task.CompletedTask;
      });
      service.RemoveHook(name);
      MockPack.SetupContainerStart();

      await service.StartAsync(TestContext.Current.CancellationToken);

      Assert.False(fired);
    }
  }
}
