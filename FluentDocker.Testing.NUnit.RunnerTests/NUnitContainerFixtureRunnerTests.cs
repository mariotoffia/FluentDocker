using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.NUnit;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace FluentDocker.Testing.NUnit.RunnerTests
{
  /// <summary>
  /// Proves NUnit runs <see cref="NUnitContainerFixtureBase"/> against a real daemon.
  /// </summary>
  [TestFixture]
  [Category("Integration")]
  public class NUnitContainerFixtureRunnerTests : NUnitContainerFixtureBase
  {
    protected override void ConfigureContainer(IContainerBuilder builder)
    {
      builder
          .UseImage("busybox:latest")
          .WithCommand("sh", "-c", "while true; do sleep 1; done");
    }

    [Test]
    public async Task FixtureInitializesContainer()
    {
      Assert.That(Container, Is.Not.Null);
      var inspected = await Container.InspectAsync(TestContext.CurrentContext.CancellationToken);
      Assert.That(inspected?.State?.Running, Is.True);
    }
  }

  [TestFixture]
  [Category("Unit")]
  public class NUnitUnavailableContainerFixtureRunnerTests : NUnitContainerFixtureBase
  {
    protected override bool SkipWhenUnavailable => true;

    protected override Func<Task<FluentDockerKernel>>? KernelFactory =>
        () => UnavailableKernel.CreateAsync();

    protected override void ConfigureContainer(IContainerBuilder builder)
    {
      builder.UseImage("alpine:latest");
    }

    [Test]
    public void FixtureIsIgnoredWhenDockerIsUnavailable()
    {
      Assert.Fail("NUnit OneTimeSetUp should ignore the fixture before test body execution.");
    }
  }

  [TestFixture]
  [Category("Unit")]
  public class NUnitMissingBinaryContainerFixtureRunnerTests : NUnitContainerFixtureBase
  {
    protected override bool SkipWhenUnavailable => true;

    protected override Func<Task<FluentDockerKernel>>? KernelFactory =>
        () => Task.FromException<FluentDockerKernel>(
            new DriverNotAvailableException("docker", "docker binary not found"));

    protected override void ConfigureContainer(IContainerBuilder builder)
    {
      builder.UseImage("alpine:latest");
    }

    [Test]
    public void FixtureIsIgnoredWhenDockerBinaryIsMissing()
    {
      Assert.Fail("NUnit OneTimeSetUp should ignore the fixture before test body execution.");
    }
  }

  internal sealed class UnavailablePack : IDriverPack
  {
    public DriverType Type => DriverType.DockerCli;

    public RuntimeType Runtime => RuntimeType.Docker;

    public Task InitializeAsync(DriverContext context, System.Threading.CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<DriverCapabilities> GetCapabilitiesAsync(System.Threading.CancellationToken cancellationToken = default) =>
        Task.FromResult(DriverCapabilities.Default());

    public Task<bool> IsHealthyAsync(System.Threading.CancellationToken cancellationToken = default) =>
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
