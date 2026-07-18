using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Testing.Core;
using Xunit;

namespace FluentDocker.Testing.Xunit
{
  /// <summary>
  /// Abstract base class for xUnit class/collection fixtures backed by a
  /// single container. Implements <see cref="IAsyncLifetime"/> so xUnit
  /// handles initialization and disposal automatically — no sync-over-async
  /// <c>GetAwaiter().GetResult()</c> needed.
  /// </summary>
  /// <remarks>
  /// <para>Subclass this and override <see cref="ConfigureContainer"/> to
  /// specify the container image and settings. Use the subclass with
  /// <c>IClassFixture&lt;T&gt;</c> or <c>ICollectionFixture&lt;T&gt;</c>.</para>
  /// <para>Usage:</para>
  /// <code>
  /// public class NginxFixture : XunitContainerFixtureBase
  /// {
  ///   protected override void ConfigureContainer(IContainerBuilder b)
  ///       =&gt; b.UseImage("nginx:latest");
  /// }
  ///
  /// public class NginxTests : IClassFixture&lt;NginxFixture&gt;
  /// {
  ///   private readonly NginxFixture _fixture;
  ///   public NginxTests(NginxFixture fixture) =&gt; _fixture = fixture;
  ///
  ///   [Fact]
  ///   public void Container_IsRunning()
  ///       =&gt; Assert.NotNull(_fixture.Container);
  /// }
  /// </code>
  /// <para>xUnit v3 does not convert class-fixture initialization failures into
  /// skipped tests. Probe before creating the resource (for example with
  /// <see cref="XunitConditionalContainerFixtureBase"/>) and then call
  /// <c>Assert.SkipWhen(fixture.IsSkipped, fixture.SkipReason);</c> in the test body.</para>
  /// </remarks>
  public abstract class XunitContainerFixtureBase : IAsyncLifetime
  {
    private static readonly TimeSpan AvailabilityProbeTimeout = TimeSpan.FromSeconds(10);
    private readonly object _availabilityLock = new();
    private ContainerResource? _resource;
    private FluentDockerKernel? _kernel;
    private Task<bool>? _dockerAvailable;

    /// <summary>
    /// The underlying container resource, available after initialization.
    /// </summary>
    public ContainerResource Resource
    {
      get { EnsureInitialized(); return _resource!; }
    }

    /// <summary>
    /// Shorthand access to the running container service.
    /// </summary>
    public IContainerService Container => Resource.Container;

    /// <summary>
    /// The kernel managing drivers for this fixture.
    /// </summary>
    public FluentDockerKernel Kernel
    {
      get { EnsureInitialized(); return _kernel!; }
    }

    /// <summary>
    /// Override to configure the container. Called during initialization.
    /// </summary>
    protected abstract void ConfigureContainer(IContainerBuilder builder);

    /// <summary>
    /// Override to provide custom resource options. Returns null for defaults.
    /// </summary>
    protected virtual DockerResourceOptions? GetOptions() => null;

    /// <summary>
    /// Override to provide a custom kernel factory.
    /// Returns null to use the default Docker CLI kernel.
    /// </summary>
    protected virtual Func<Task<FluentDockerKernel>>? KernelFactory => null;

    /// <summary>
    /// Probes availability of the fixture's configured runtime (Docker or Podman) for
    /// test-body skip checks. Uses the driver selected by <see cref="GetOptions"/> — or
    /// the kernel's default driver when none is selected — so Podman/custom-id fixtures
    /// probe the right daemon instead of hard-coding Docker.
    /// </summary>
    /// <remarks>
    /// <c>false</c> means "runtime unreachable" — a skippable environmental condition. A
    /// FIXTURE MISCONFIGURATION is deliberately not mapped to <c>false</c>: selecting a
    /// specific driver id (e.g. <c>DriverSelection.Specific("podman-cli")</c>) without also
    /// overriding <see cref="KernelFactory"/> to register that driver throws
    /// <c>DriverNotFoundException</c> from <c>Assert.SkipWhen</c> instead of silently
    /// skipping the suite forever. Override <see cref="KernelFactory"/> together with any
    /// custom driver id.
    /// </remarks>
    public Task<bool> IsDockerAvailableAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      Task<bool> probe;
      lock (_availabilityLock)
      {
        if (_dockerAvailable == null ||
            _dockerAvailable.IsFaulted ||
            _dockerAvailable.IsCanceled)
          _dockerAvailable = ProbeDockerAvailableAsync();
        probe = _dockerAvailable;
      }
      return cancellationToken.CanBeCanceled ? probe.WaitAsync(cancellationToken) : probe;
    }

    private async Task<bool> ProbeDockerAvailableAsync()
    {
      var driver = GetOptions()?.Driver;
      var driverId = driver is not null && !driver.UseDefault ? driver.DriverId : null;
      using var cts = new CancellationTokenSource(AvailabilityProbeTimeout);
      try
      {
        return await DockerAvailability.IsAvailableAsync(
            KernelFactory!, driverId!, cts.Token).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (cts.IsCancellationRequested)
      {
        return false;
      }
    }

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
      if (_resource != null)
        throw new InvalidOperationException(
            "Already initialized. Dispose before re-initializing.");

      // ponytail: xUnit v3 invokes fixture InitializeAsync once; keep only sequential misuse guard.
      var (kernel, resource) = await ResourceLifecycle.CreateAndInitializeAsync(
          k => new ContainerResource(k, ConfigureContainer, GetOptions()!),
          KernelFactory!).ConfigureAwait(false);

      _kernel = kernel;
      _resource = resource;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
      // Clear handles only AFTER successful disposal. If cleanup throws, the public
      // Resource/Kernel handles stay non-null for LastTeardownDiagnostics and label-based
      // manual cleanup (docker/podman rm -f by the session label). The kernel is already
      // disposed by ResourceLifecycle.DisposeAsync, so it cannot be reused to retry teardown;
      // recover via the next run's orphan sweep or a manual label sweep. The exception propagates.
      await ResourceLifecycle.DisposeAsync(_resource!, _kernel!).ConfigureAwait(false);
      _resource = null;
      _kernel = null;
      GC.SuppressFinalize(this);
    }

    private void EnsureInitialized()
    {
      if (_resource == null)
        throw new InvalidOperationException(
            "Fixture has not been initialized. Call InitializeAsync first.");
    }
  }
}
