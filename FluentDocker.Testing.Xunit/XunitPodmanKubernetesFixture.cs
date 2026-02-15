using System;
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
  public class XunitPodmanKubernetesFixture : IAsyncDisposable
  {
    private PodmanKubernetesResource _resource;

    /// <summary>
    /// The underlying Podman Kubernetes resource.
    /// </summary>
    public PodmanKubernetesResource Resource => _resource;

    /// <summary>
    /// Path to the Kubernetes YAML file.
    /// </summary>
    public string YamlPath => _resource?.YamlPath;

    /// <summary>
    /// The kernel managing drivers.
    /// </summary>
    public FluentDockerKernel Kernel { get; private set; }

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
        DockerResourceOptions options = null)
    {
      if (_resource != null)
        throw new InvalidOperationException(
            "Fixture has already been initialized. Dispose before re-initializing.");

      FluentDockerKernel kernel = null;
      try
      {
        kernel = kernelFactory != null
            ? await kernelFactory()
            : await FluentDockerKernel.Create()
                .WithPodmanCli("podman-cli", d => d.AsDefault())
                .BuildAsync();

        var resource = new PodmanKubernetesResource(kernel, config, options);
        await resource.InitializeAsync();

        Kernel = kernel;
        _resource = resource;
      }
      catch
      {
        kernel?.Dispose();
        throw;
      }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
      if (_resource != null)
        await _resource.DisposeAsync();

      Kernel?.Dispose();
    }
  }
}
