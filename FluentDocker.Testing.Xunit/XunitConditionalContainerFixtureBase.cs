using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Testing.Core;
using Xunit;

namespace FluentDocker.Testing.Xunit
{
  /// <summary>
  /// xUnit container fixture that probes Docker before provisioning.
  /// </summary>
  /// <remarks>
  /// Use <see cref="IsSkipped"/> in each test body:
  /// <c>Assert.SkipWhen(fixture.IsSkipped, fixture.SkipReason);</c>.
  /// </remarks>
  public abstract class XunitConditionalContainerFixtureBase : IAsyncLifetime
  {
    private static readonly TimeSpan HealthProbeTimeout = TimeSpan.FromSeconds(10);
    private ContainerResource? _resource;
    private FluentDockerKernel? _kernel;

    /// <summary>True when the fixture skipped provisioning because Docker is unavailable.</summary>
    public bool IsSkipped { get; private set; }

    /// <summary>Reason to pass to <c>Assert.SkipWhen</c>.</summary>
    public string SkipReason { get; private set; } = "Docker-compatible runtime is unavailable.";

    /// <summary>The underlying resource, available when not skipped and initialized.</summary>
    public ContainerResource Resource
    {
      get { EnsureInitialized(); return _resource!; }
    }

    /// <summary>Shorthand access to the running container.</summary>
    public IContainerService Container => Resource.Container;

    /// <summary>The owned kernel, available when not skipped and initialized.</summary>
    public FluentDockerKernel Kernel
    {
      get { EnsureInitialized(); return _kernel!; }
    }

    /// <summary>Override to configure the container.</summary>
    protected abstract void ConfigureContainer(IContainerBuilder builder);

    /// <summary>Override to provide custom resource options.</summary>
    protected virtual DockerResourceOptions? GetOptions() => null;

    /// <summary>Override to provide a custom kernel factory.</summary>
    protected virtual Func<Task<FluentDockerKernel>>? KernelFactory => null;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
      var kernelFactory = KernelFactory ?? (() => ResourceLifecycle.CreateDefaultDockerKernelAsync());
      var options = GetOptions() ?? new DockerResourceOptions();
      FluentDockerKernel? kernel = null;
      ContainerResource? resource = null;
      try
      {
        kernel = await kernelFactory().ConfigureAwait(false);
        var driverId = options.Driver.UseDefault
            ? kernel.DefaultDriverId
            : options.Driver.DriverId;
        driverId ??= kernel.DefaultDriverId;
        if (driverId == null)
        {
          // No selected driver and no default registered: nothing to probe — skip with an
          // actionable reason instead of erroring on a null driver id.
          await SkipAsync(kernel, "No driver is registered on the kernel (no default driver id).").ConfigureAwait(false);
          return;
        }

        bool healthy;
        using (var healthCts = new CancellationTokenSource())
        {
          var healthProbe = CapabilityChecks.IsHealthyAsync(kernel, driverId, healthCts.Token);
          try
          {
            healthy = await healthProbe.WaitAsync(HealthProbeTimeout)
                .ConfigureAwait(false);
          }
          catch (TimeoutException)
          {
            healthCts.Cancel();
            Observe(healthProbe);
            await SkipAsync(kernel, $"Docker driver '{driverId}' health probe timed out.").ConfigureAwait(false);
            return;
          }
          catch (OperationCanceledException) when (healthCts.IsCancellationRequested)
          {
            Observe(healthProbe);
            await SkipAsync(kernel, $"Docker driver '{driverId}' health probe timed out.").ConfigureAwait(false);
            return;
          }
        }
        if (!healthy)
        {
          await SkipAsync(kernel, $"Docker driver '{driverId}' is not reachable.").ConfigureAwait(false);
          return;
        }

        resource = new ContainerResource(kernel, ConfigureContainer, options);
        await resource.InitializeAsync().ConfigureAwait(false);
        _kernel = kernel;
        _resource = resource;
        kernel = null;
        resource = null;
      }
      catch (DriverNotAvailableException ex)
      {
        await ResourceLifecycle.DisposeAsync(resource, kernel).ConfigureAwait(false);
        IsSkipped = true;
        SkipReason = ex.Message;
      }
      catch (ResourceInitializationException ex)
          when (ex.InnerException is FluentDockerUnavailableException)
      {
        // The daemon can die between the health probe and resource init: the unavailability
        // then arrives WRAPPED by the resource layer. Skip (mirroring the MSTest/NUnit
        // adapters) instead of erroring the whole class on a flaky CI daemon.
        await ResourceLifecycle.DisposeAsync(resource, kernel).ConfigureAwait(false);
        IsSkipped = true;
        SkipReason = ex.InnerException.Message;
      }
      catch
      {
        await ResourceLifecycle.DisposeAsync(resource, kernel).ConfigureAwait(false);
        throw;
      }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
      await ResourceLifecycle.DisposeAsync(_resource, _kernel).ConfigureAwait(false);
      _resource = null;
      _kernel = null;
      GC.SuppressFinalize(this);
    }

    private async Task SkipAsync(FluentDockerKernel kernel, string reason)
    {
      IsSkipped = true;
      SkipReason = reason;
      await kernel.DisposeAsync().ConfigureAwait(false);
    }

    private void EnsureInitialized()
    {
      if (_resource == null)
        throw new InvalidOperationException(
            "Fixture has not been initialized. Check IsSkipped before accessing Resource.");
    }

    private static void Observe(Task task)
    {
      _ = task.ContinueWith(
          t => _ = t.Exception,
          CancellationToken.None,
          TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
          TaskScheduler.Default);
    }
  }
}
