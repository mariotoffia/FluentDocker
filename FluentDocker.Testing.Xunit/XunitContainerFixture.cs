using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Testing.Core;

namespace FluentDocker.Testing.Xunit
{
  /// <summary>
  /// xUnit fixture that wraps a <see cref="ContainerResource"/>.
  /// Use with <c>IClassFixture&lt;XunitContainerFixture&gt;</c> or
  /// <c>ICollectionFixture&lt;XunitContainerFixture&gt;</c>.
  /// </summary>
  public class XunitContainerFixture : IAsyncDisposable
  {
    private ContainerResource _resource;

    /// <summary>
    /// The underlying container resource.
    /// </summary>
    public ContainerResource Resource => _resource;

    /// <summary>
    /// The running container service, available after initialization.
    /// </summary>
    public IContainerService Container => _resource?.Container;

    /// <summary>
    /// The kernel managing drivers.
    /// </summary>
    public FluentDockerKernel Kernel { get; private set; }

    /// <summary>
    /// Configures and initializes the fixture.
    /// Call this from your fixture constructor or a setup method.
    /// </summary>
    /// <param name="configure">Container builder configuration.</param>
    /// <param name="kernelFactory">Optional kernel factory. Defaults to Docker CLI.</param>
    /// <param name="options">Optional resource options.</param>
    public async Task InitializeAsync(
        Action<IContainerBuilder> configure,
        Func<Task<FluentDockerKernel>> kernelFactory = null,
        DockerResourceOptions options = null)
    {
      if (_resource != null)
        throw new InvalidOperationException(
            "Fixture has already been initialized. Dispose before re-initializing.");

      FluentDockerKernel kernel = null;
      ContainerResource resource = null;
      try
      {
        kernel = kernelFactory != null
            ? await kernelFactory()
            : await FluentDockerKernel.Create()
                .WithDockerCli("docker-cli", d => d.AsDefault())
                .BuildAsync();

        resource = new ContainerResource(kernel, configure, options);
        await resource.InitializeAsync();

        Kernel = kernel;
        _resource = resource;
      }
      catch
      {
        try
        { if (resource != null) await resource.DisposeAsync(); }
        catch { /* best effort */ }
        kernel?.Dispose();
        throw;
      }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
      try
      {
        if (_resource != null)
          await _resource.DisposeAsync();
      }
      finally
      {
        Kernel?.Dispose();
        _resource = null;
        Kernel = null;
      }
    }
  }
}
