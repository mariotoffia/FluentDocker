using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;
using Xunit;

namespace FluentDocker.Testing.Xunit
{
  /// <summary>
  /// xUnit fixture that wraps a <see cref="PodmanKubernetesResource"/>.
  /// Use with <c>IClassFixture&lt;XunitPodmanKubernetesFixture&gt;</c> or
  /// <c>ICollectionFixture&lt;XunitPodmanKubernetesFixture&gt;</c>.
  /// </summary>
  /// <remarks>
  /// <para>This fixture requires explicit configuration via <see cref="Configure"/>
  /// (recommended) or <see cref="InitializeAsync(KubePlayConfig,Func{Task{FluentDockerKernel}},DockerResourceOptions,CancellationToken)"/>
  /// (manual). No abstract fixture base is provided for Podman Kubernetes because
  /// it uses a config object rather than a builder.</para>
  /// <para>Accessing properties before initialization throws
  /// <see cref="InvalidOperationException"/>.</para>
  /// </remarks>
  public class XunitPodmanKubernetesFixture : IAsyncLifetime
  {
    private PodmanKubernetesResource _resource;
    private FluentDockerKernel _kernel;
    private KubePlayConfig _deferredConfig;
    private Func<Task<FluentDockerKernel>> _deferredKernelFactory;
    private DockerResourceOptions _deferredOptions;
    private bool _configured;

    /// <summary>
    /// The underlying Podman Kubernetes resource.
    /// </summary>
    public PodmanKubernetesResource Resource
    {
      get { EnsureInitialized(); return _resource; }
    }

    /// <summary>
    /// Path to the Kubernetes YAML file.
    /// </summary>
    public string YamlPath
    {
      get { EnsureInitialized(); return _resource.YamlPath; }
    }

    /// <summary>
    /// The kernel managing drivers.
    /// </summary>
    public FluentDockerKernel Kernel
    {
      get { EnsureInitialized(); return _kernel; }
    }

    /// <summary>
    /// Stores configuration for deferred initialization via <see cref="IAsyncLifetime"/>.
    /// Call this in your fixture constructor, then let xUnit call
    /// <see cref="IAsyncLifetime.InitializeAsync"/>.
    /// </summary>
    /// <param name="config">Kubernetes play configuration.</param>
    /// <param name="kernelFactory">Optional kernel factory. Defaults to Podman CLI.</param>
    /// <param name="options">Optional resource options.</param>
    /// <returns>This fixture for fluent chaining.</returns>
    public XunitPodmanKubernetesFixture Configure(
        KubePlayConfig config,
        Func<Task<FluentDockerKernel>> kernelFactory = null,
        DockerResourceOptions options = null)
    {
      _deferredConfig = config ?? throw new ArgumentNullException(nameof(config));
      _deferredKernelFactory = kernelFactory;
      _deferredOptions = options;
      _configured = true;
      return this;
    }

    /// <summary>
    /// Called by xUnit when using <c>IClassFixture</c> or <c>ICollectionFixture</c>.
    /// Requires <see cref="Configure"/> to have been called first; otherwise no-ops.
    /// </summary>
    async ValueTask IAsyncLifetime.InitializeAsync()
    {
      if (!_configured)
        return;
      await InitializeAsync(_deferredConfig, _deferredKernelFactory, _deferredOptions);
    }

    /// <summary>
    /// Configures and initializes the fixture immediately.
    /// </summary>
    public async Task InitializeAsync(
        KubePlayConfig config,
        Func<Task<FluentDockerKernel>> kernelFactory = null,
        DockerResourceOptions options = null,
        CancellationToken cancellationToken = default)
    {
      if (_resource != null)
        throw new InvalidOperationException(
            "Fixture has already been initialized. Dispose before re-initializing.");

      var (kernel, resource) = await ResourceLifecycle.CreateAndInitializeAsync(
          k => new PodmanKubernetesResource(k, config, options),
          kernelFactory,
          ResourceLifecycle.CreateDefaultPodmanKernelAsync,
          cancellationToken);

      _kernel = kernel;
      _resource = resource;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
      try
      {
        await ResourceLifecycle.DisposeAsync(_resource, _kernel);
      }
      finally
      {
        _resource = null;
        _kernel = null;
      }
    }

    private void EnsureInitialized()
    {
      if (_resource == null)
        throw new InvalidOperationException(
            "Fixture has not been initialized. Call InitializeAsync first.");
    }
  }
}
