using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;

namespace FluentDocker.Testing.Xunit
{
  /// <summary>
  /// xUnit fixture that wraps a <see cref="PodmanKubernetesResource"/>.
  /// Use with <c>IClassFixture&lt;XunitPodmanKubernetesFixture&gt;</c> or
  /// <c>ICollectionFixture&lt;XunitPodmanKubernetesFixture&gt;</c>.
  /// </summary>
  /// <remarks>
  /// <para>This fixture requires explicit initialization via
  /// <see cref="InitializeAsync"/>. No abstract fixture base is provided for
  /// Podman Kubernetes because it uses a config object rather than a builder.</para>
  /// <para>You <b>must</b> call <see cref="InitializeAsync"/> before accessing
  /// any properties — accessing them before initialization throws
  /// <see cref="InvalidOperationException"/>.</para>
  /// </remarks>
  public class XunitPodmanKubernetesFixture : IAsyncDisposable
  {
    private PodmanKubernetesResource _resource;
    private FluentDockerKernel _kernel;

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
    /// Configures and initializes the fixture.
    /// Call this from your fixture constructor or a setup method.
    /// </summary>
    /// <param name="config">Kubernetes play configuration.</param>
    /// <param name="kernelFactory">Optional kernel factory. Defaults to Podman CLI.</param>
    /// <param name="options">Optional resource options.</param>
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
