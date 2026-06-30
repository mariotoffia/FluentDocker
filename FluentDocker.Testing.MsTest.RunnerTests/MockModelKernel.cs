using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FluentDocker.Testing.MsTest.RunnerTests
{
  /// <summary>
  /// A minimal, self-contained mock <see cref="IDriverPack"/> that resolves only the
  /// three Docker Model Runner ports. The full <c>MockDriverPack</c> lives in the
  /// (non-referenceable) Exe test project, so this is the smallest pack the MSTest
  /// runner tests need to drive a <see cref="FluentDocker.Testing.Core.ModelResource"/>
  /// through initialize and dispose.
  /// </summary>
  // ponytail: model ports only. Add container/network ports the day a runner test needs them.
  public sealed class MockModelDriverPack : IDriverPack
  {
    private readonly Dictionary<Type, object> _drivers = [];
    private bool _initialized;

    /// <summary>The mock model-runtime driver (Load / Unload live here).</summary>
    public Mock<IModelRuntimeDriver> RuntimeDriver { get; } = new Mock<IModelRuntimeDriver>();

    /// <summary>The mock model-management driver.</summary>
    public Mock<IModelManagementDriver> ManagementDriver { get; } = new Mock<IModelManagementDriver>();

    /// <summary>The mock model-inference driver.</summary>
    public Mock<IModelInferenceDriver> InferenceDriver { get; } = new Mock<IModelInferenceDriver>();

    /// <summary>Creates the pack and sets up Load / Unload to succeed.</summary>
    public MockModelDriverPack()
    {
      RuntimeDriver
          .Setup(d => d.LoadAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
              It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      RuntimeDriver
          .Setup(d => d.UnloadAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      RuntimeDriver
          .Setup(d => d.UnloadAllAsync(It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
    }

    /// <inheritdoc />
    public DriverType Type => DriverType.DockerCli;

    /// <inheritdoc />
    public RuntimeType Runtime => RuntimeType.Docker;

    /// <inheritdoc />
    public Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default)
    {
      _drivers[typeof(IModelRuntimeDriver)] = RuntimeDriver.Object;
      _drivers[typeof(IModelManagementDriver)] = ManagementDriver.Object;
      _drivers[typeof(IModelInferenceDriver)] = InferenceDriver.Object;
      _initialized = true;
      return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(DriverCapabilities.Default());

    /// <inheritdoc />
    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    /// <inheritdoc />
    public T SysCtl<T>(string driverId) where T : class
    {
      EnsureInitialized();
      if (_drivers.TryGetValue(typeof(T), out var driver))
        return (T)driver;
      throw new InterfaceNotSupportedException(driverId, typeof(T).Name);
    }

    /// <inheritdoc />
    public object SysCtl(string driverId, Type interfaceType)
    {
      EnsureInitialized();
      if (_drivers.TryGetValue(interfaceType, out var driver))
        return driver;
      throw new InterfaceNotSupportedException(driverId, interfaceType.Name);
    }

    /// <inheritdoc />
    public bool TrySysCtl<T>(string driverId, out T instance) where T : class
    {
      EnsureInitialized();
      if (_drivers.TryGetValue(typeof(T), out var driver))
      {
        instance = (T)driver;
        return true;
      }
      instance = null!;
      return false;
    }

    /// <inheritdoc />
    public bool TryResolve(Type interfaceType, out object implementation)
    {
      EnsureInitialized();
      return _drivers.TryGetValue(interfaceType, out implementation!);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<Type> GetSupportedInterfaces()
    {
      EnsureInitialized();
      return _drivers.Keys.ToList().AsReadOnly();
    }

    private void EnsureInitialized()
    {
      if (!_initialized)
        throw new InvalidOperationException("MockModelDriverPack not initialized. Call InitializeAsync first.");
    }
  }

  /// <summary>
  /// Builds a <see cref="FluentDockerKernel"/> backed by a <see cref="MockModelDriverPack"/>.
  /// Mirrors <c>MockKernelBuilderExtensions.CreateWithMockDriverAsync</c> from the Exe test project.
  /// </summary>
  public static class MockModelKernel
  {
    /// <summary>Creates a kernel with the given (or a fresh) mock model pack as the default driver.</summary>
    public static async Task<(FluentDockerKernel kernel, MockModelDriverPack pack)> CreateAsync(
        MockModelDriverPack? pack = null, string driverId = "docker")
    {
      pack ??= new MockModelDriverPack();
      var context = new DriverContext(driverId);
      await pack.InitializeAsync(context);

      var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(driverId, pack, context);
      kernel.SetDefaultDriver(driverId);

      return (kernel, pack);
    }
  }
}
