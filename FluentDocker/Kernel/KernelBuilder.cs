using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Kernel
{
  /// <summary>
  /// Fluent builder for creating and configuring a FluentDockerKernel.
  /// </summary>
  public class KernelBuilder : IKernelBuilder
  {
    private readonly List<DriverConfiguration> _driverConfigurations = [];
    private readonly ILoggerFactory _loggerFactory;
    private int _built;

    /// <summary>
    /// Creates a new kernel builder with the consumer-supplied logger factory.
    /// </summary>
    /// <param name="loggerFactory">Logger factory; required. Pass
    /// <see cref="Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance"/>
    /// to suppress all logging from the constructed kernel.</param>
    public KernelBuilder(ILoggerFactory loggerFactory)
    {
      ArgumentNullException.ThrowIfNull(loggerFactory);
      _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public IKernelBuilder WithDockerCli(string driverId, Action<IDockerCliDriverBuilder> configure)
    {
      ValidateDriverArgs(driverId, configure);
      var builder = new DockerCliDriverBuilder(driverId);
      configure(builder);
      _driverConfigurations.Add(builder.Build());
      return this;
    }

    /// <inheritdoc />
    public IKernelBuilder WithDockerApi(string driverId, Action<IDockerApiDriverBuilder> configure)
    {
      ValidateDriverArgs(driverId, configure);
      var builder = new DockerApiDriverBuilder(driverId);
      configure(builder);
      _driverConfigurations.Add(builder.Build());
      return this;
    }

    /// <inheritdoc />
    public IKernelBuilder WithPodmanCli(string driverId, Action<IPodmanCliDriverBuilder> configure)
    {
      ValidateDriverArgs(driverId, configure);
      var builder = new PodmanCliDriverBuilder(driverId);
      configure(builder);
      _driverConfigurations.Add(builder.Build());
      return this;
    }

    /// <inheritdoc />
    public IKernelBuilder WithDriver(string driverId, Action<IDriverBuilder> configure)
    {
      ValidateDriverArgs(driverId, configure);
      var driverBuilder = new DriverBuilder(driverId);
      configure(driverBuilder);
      _driverConfigurations.Add(driverBuilder.Build());
      return this;
    }

    /// <summary>
    /// Builds the kernel synchronously (TERMINAL operation).
    /// </summary>
    /// <remarks>
    /// For async contexts (ASP.NET, UI applications), prefer <see cref="BuildAsync"/> to avoid deadlocks.
    /// This method is safe to use in console apps, test fixtures, and scripts.
    /// </remarks>
    public FluentDockerKernel Build()
    {
      return Task.Run(() => BuildAsync()).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async Task<FluentDockerKernel> BuildAsync(CancellationToken cancellationToken = default)
    {
      if (Interlocked.Exchange(ref _built, 1) != 0)
        throw new InvalidOperationException("KernelBuilder is single-use; create a new builder for another kernel.");

      var kernel = new FluentDockerKernel(new DriverRegistry(_loggerFactory), _loggerFactory);
      var configIndex = 0;
      object? currentInstance = null;
      var currentRegistered = false;
      // Only instances the builder itself created (the WithDockerCli/Api/PodmanCli factory packs)
      // are builder-owned. UseCustomDriver/UseCustomDriverPack instances are user-owned and must
      // NOT be disposed on a pre-acceptance failure — the user still holds the reference (KRN-MAJ-4).
      var currentOwnedByBuilder = false;
      var registeredInstances = new HashSet<object>(ReferenceEqualityComparer.Instance);

      try
      {
        for (; configIndex < _driverConfigurations.Count; configIndex++)
        {
          var config = _driverConfigurations[configIndex];
          currentInstance = null;
          currentRegistered = false;
          currentOwnedByBuilder = false;
          var driverPack = config.DriverPackFactory?.Invoke() ?? config.DriverPack;
          if (driverPack != null)
          {
            currentInstance = driverPack;
            currentOwnedByBuilder = config.DriverPackFactory != null;
            await kernel.RegisterDriverPackAsync(
                config.DriverId, driverPack, config.Context, cancellationToken).ConfigureAwait(false);
            currentRegistered = true;
            registeredInstances.Add(driverPack);
          }
          else if (config.Driver != null)
          {
            currentInstance = config.Driver;
            await kernel.RegisterDriverAsync(
                config.DriverId, config.Driver, config.Context, cancellationToken).ConfigureAwait(false);
            currentRegistered = true;
            registeredInstances.Add(config.Driver);
          }

          if (config.IsDefault)
            kernel.SetDefaultDriver(config.DriverId);
        }
      }
      catch (Exception ex)
      {
        // Dispose only the builder-created factory pack that failed to register. User-supplied
        // instances (current or later, unregistered) are left intact for the caller to reuse or
        // dispose — a duplicate-id typo must not destroy the user's driver (KRN-MAJ-4).
        if (!currentRegistered &&
            currentInstance != null &&
            currentOwnedByBuilder &&
            !registeredInstances.Contains(currentInstance) &&
            !DriverRegistry.RegistrationFailureDisposedInstance(ex))
        {
          var logger = _loggerFactory.CreateLogger<KernelBuilder>();
          await DisposeOwnedInstanceAsync(
              currentInstance, logger, _driverConfigurations[configIndex].DriverId).ConfigureAwait(false);
        }
        await kernel.DisposeAsync().ConfigureAwait(false);
        throw;
      }

      return kernel;
    }

    private void ValidateDriverArgs<T>(string driverId, Action<T> configure)
    {
      ThrowIfBuilt();
      if (string.IsNullOrWhiteSpace(driverId))
        throw new ArgumentException("Driver ID cannot be null or empty", nameof(driverId));
      ArgumentNullException.ThrowIfNull(configure);
    }

    private void ThrowIfBuilt()
    {
      if (Volatile.Read(ref _built) != 0)
        throw new InvalidOperationException("KernelBuilder is single-use; create a new builder for another kernel.");
    }

    private static async Task DisposeOwnedInstanceAsync(object instance, ILogger logger, string driverId)
    {
      try
      {
        if (instance is IAsyncDisposable asyncDisposable)
          await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (instance is IDisposable disposable)
          await Task.Run(disposable.Dispose).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Failed to dispose unregistered driver configuration {DriverId}", driverId);
      }
    }

    internal sealed class DriverConfiguration
    {
      public string DriverId { get; set; } = null!;
      public IDriver? Driver { get; set; }
      public IDriverPack? DriverPack { get; set; }
      public Func<IDriverPack>? DriverPackFactory { get; set; }
      public DriverContext Context { get; set; } = null!;
      public bool IsDefault { get; set; }
    }
  }

  /// <summary>
  /// Builder for configuring a custom driver (via <see cref="IKernelBuilder.WithDriver"/>).
  /// </summary>
  internal sealed class DriverBuilder(string driverId) : IDriverBuilder
  {
    private readonly string _driverId = driverId;
    private IDriver? _driver;
    private IDriverPack? _driverPack;
    private string? _host;
    private string? _certificatePath;
    private bool _isDefault;

    public IDriverBuilder UseCustomDriver(IDriver driver)
    {
      ArgumentNullException.ThrowIfNull(driver);
      _driver = driver;
      _driverPack = null;
      return this;
    }

    public IDriverBuilder UseCustomDriverPack(IDriverPack driverPack)
    {
      ArgumentNullException.ThrowIfNull(driverPack);
      _driverPack = driverPack;
      _driver = null;
      return this;
    }

    public IDriverBuilder AtHost(string host)
    {
      _host = host;
      return this;
    }

    public IDriverBuilder WithCertificates(string certificatePath)
    {
      _certificatePath = certificatePath;
      return this;
    }

    public IDriverBuilder AsDefault()
    {
      _isDefault = true;
      return this;
    }

    internal KernelBuilder.DriverConfiguration Build()
    {
      if (_driver == null && _driverPack == null)
      {
        throw new InvalidOperationException(
            $"No driver or driver pack specified for driver ID '{_driverId}'");
      }

      var context = new DriverContext(_driverId)
      {
        Host = _host,
        CertificatePath = _certificatePath,
      };

      return new KernelBuilder.DriverConfiguration
      {
        DriverId = _driverId,
        Driver = _driver,
        DriverPack = _driverPack,
        Context = context,
        IsDefault = _isDefault,
      };
    }
  }
}
