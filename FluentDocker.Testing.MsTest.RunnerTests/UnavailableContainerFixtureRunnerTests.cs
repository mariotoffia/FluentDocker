using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.MsTest;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDocker.Testing.MsTest.RunnerTests
{
  [TestClass]
  [TestCategory("Unit")]
  public class UnavailableContainerFixtureRunnerTests : MsTestPerTestContainerFixtureBase
  {
    protected override bool SkipWhenUnavailable => true;

    protected override Func<Task<FluentDockerKernel>>? KernelFactory =>
        () => UnavailableKernel.CreateAsync();

    protected override void ConfigureContainer(IContainerBuilder builder)
    {
      builder.UseImage("alpine:latest");
    }

    [TestMethod]
    public void FixtureIsInconclusiveWhenDockerIsUnavailable()
    {
      Assert.Fail("MSTest TestInitialize should mark the test inconclusive before test body execution.");
    }
  }

  [TestClass]
  [TestCategory("Unit")]
  public class MissingBinaryContainerFixtureRunnerTests : MsTestPerTestContainerFixtureBase
  {
    protected override bool SkipWhenUnavailable => true;

    protected override Func<Task<FluentDockerKernel>>? KernelFactory =>
        () => Task.FromException<FluentDockerKernel>(
            new DriverNotAvailableException("docker", "docker binary not found"));

    protected override void ConfigureContainer(IContainerBuilder builder)
    {
      builder.UseImage("alpine:latest");
    }

    [TestMethod]
    public void FixtureIsInconclusiveWhenDockerBinaryIsMissing()
    {
      Assert.Fail("MSTest TestInitialize should mark the test inconclusive before test body execution.");
    }
  }

  internal sealed class UnavailablePack : IDriverPack
  {
    public DriverType Type => DriverType.DockerCli;

    public RuntimeType Runtime => RuntimeType.Docker;

    public Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(DriverCapabilities.Default());

    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public T SysCtl<T>(string driverId) where T : class =>
        throw new InterfaceNotSupportedException(driverId, typeof(T).Name);

    public object SysCtl(string driverId, Type interfaceType) =>
        throw new InterfaceNotSupportedException(driverId, interfaceType.Name);

    public bool TrySysCtl<T>(string driverId, out T instance) where T : class
    {
      instance = null!;
      return false;
    }

    public bool TryResolve(Type interfaceType, out object implementation)
    {
      implementation = null!;
      return false;
    }

    public IReadOnlyCollection<Type> GetSupportedInterfaces() => [];
  }

  internal static class UnavailableKernel
  {
    public static async Task<FluentDockerKernel> CreateAsync()
    {
      var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var context = new DriverContext("docker");
      await kernel.RegisterDriverPackAsync("docker", new UnavailablePack(), context).ConfigureAwait(false);
      kernel.SetDefaultDriver("docker");
      return kernel;
    }
  }
}
