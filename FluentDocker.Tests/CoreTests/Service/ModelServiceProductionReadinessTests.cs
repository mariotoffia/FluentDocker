using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public class ModelServiceProductionReadinessTests
  {
    private static readonly ModelReference Model = ModelReference.Parse("ai/smollm2");

    [Fact]
    public async Task StartAsync_AfterHardLoadFailure_RetriesAndLoads()
    {
      await using var kernel = new FluentDocker.Kernel.FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var runner = new Mock<IModelRunner>();
      var calls = 0;
      runner.Setup(r => r.LoadAsync(
              It.IsAny<ModelReference>(), It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .Returns(() =>
          {
            calls++;
            if (calls == 1)
              throw new InvalidOperationException("transient load failure");
            return Task.CompletedTask;
          });
      runner.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
      var service = new ModelService(kernel, "docker", Model, runner.Object, null!, keepRunning: true);

      await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(TestContext.Current.CancellationToken));
      await service.StartAsync(TestContext.Current.CancellationToken);

      Assert.Equal(2, calls);
      Assert.Equal(ServiceRunningState.Running, service.State);
    }

    [Fact]
    public async Task StartAsync_AfterStop_ReloadsModel()
    {
      await using var kernel = new FluentDocker.Kernel.FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var runner = new Mock<IModelRunner>();
      var calls = 0;
      runner.Setup(r => r.LoadAsync(
              It.IsAny<ModelReference>(), It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .Returns(() =>
          {
            calls++;
            return Task.CompletedTask;
          });
      runner.Setup(r => r.UnloadAsync(It.IsAny<ModelReference>(), It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
      runner.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
      var service = new ModelService(kernel, "docker", Model, runner.Object, null!, keepRunning: true);

      await service.StartAsync(TestContext.Current.CancellationToken);
      await service.StopAsync(TestContext.Current.CancellationToken);
      await service.StartAsync(TestContext.Current.CancellationToken);

      Assert.Equal(2, calls);
      Assert.Equal(ServiceRunningState.Running, service.State);
    }
  }
}
