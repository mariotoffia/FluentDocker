using System.Collections.Generic;
using System.Threading;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using Moq;

namespace FluentDocker.Tests.Mocks
{
  /// <summary>
  /// Swarm stack mock setup helpers for MockDriverPack.
  /// </summary>
  public partial class MockDriverPack
  {
    /// <summary>
    /// Mock stack driver.
    /// </summary>
    public Mock<IStackDriver> StackDriver { get; } = new Mock<IStackDriver>();

    /// <summary>
    /// Registers and sets up the IStackDriver for testing.
    /// Call this before tests that need stack support.
    /// </summary>
    public MockDriverPack EnableStackDriver()
    {
      _drivers[typeof(IStackDriver)] = StackDriver.Object;
      return this;
    }

    /// <summary>
    /// Sets up StackDriver.DeployAsync to return success.
    /// </summary>
    public MockDriverPack SetupStackDeploy(string stackName = "test-stack")
    {
      StackDriver
          .Setup(d => d.DeployAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<StackDeployConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<StackDeployResult>.Ok(
              new StackDeployResult { StackName = stackName }));
      return this;
    }

    /// <summary>
    /// Sets up StackDriver.RemoveAsync to return success.
    /// </summary>
    public MockDriverPack SetupStackRemove()
    {
      StackDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string[]>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>
    /// Sets up StackDriver.GetServicesAsync to return the specified services.
    /// </summary>
    public MockDriverPack SetupStackGetServices(params StackServiceInfo[] services)
    {
      StackDriver
          .Setup(d => d.GetServicesAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<StackServiceFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<StackServiceInfo>>.Ok(
              [.. services]));
      return this;
    }

    /// <summary>
    /// Sets up StackDriver.GetTasksAsync to return the specified tasks.
    /// </summary>
    public MockDriverPack SetupStackGetTasks(params StackTask[] tasks)
    {
      StackDriver
          .Setup(d => d.GetTasksAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<StackTaskFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<StackTask>>.Ok(
              [.. tasks]));
      return this;
    }
  }
}
