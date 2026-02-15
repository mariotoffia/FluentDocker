using System;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;

namespace FluentDocker.Testing.Xunit
{
  /// <summary>
  /// Generic xUnit fixture for any <see cref="IDockerResource"/>.
  /// Use this for plugin resources or custom resource types.
  /// Use with <c>IClassFixture&lt;XunitResourceFixture&lt;TResource&gt;&gt;</c> or
  /// <c>ICollectionFixture&lt;XunitResourceFixture&lt;TResource&gt;&gt;</c>.
  /// </summary>
  /// <typeparam name="TResource">The resource type to manage.</typeparam>
  public class XunitResourceFixture<TResource> : IAsyncDisposable
      where TResource : class, IDockerResource
  {
    private TResource _resource;

    /// <summary>
    /// The underlying resource.
    /// </summary>
    public TResource Resource => _resource;

    /// <summary>
    /// The kernel managing drivers.
    /// </summary>
    public FluentDockerKernel Kernel { get; private set; }

    /// <summary>
    /// Configures and initializes the fixture.
    /// Call this from your fixture constructor or a setup method.
    /// </summary>
    /// <param name="resourceFactory">Factory receiving a kernel; returns the resource.</param>
    /// <param name="kernelFactory">Optional kernel factory. Defaults to Docker CLI.</param>
    public async Task InitializeAsync(
        Func<FluentDockerKernel, TResource> resourceFactory,
        Func<Task<FluentDockerKernel>> kernelFactory = null)
    {
      ArgumentNullException.ThrowIfNull(resourceFactory);

      if (_resource != null)
        throw new InvalidOperationException(
            "Fixture has already been initialized. Dispose before re-initializing.");

      FluentDockerKernel kernel = null;
      TResource resource = null;
      try
      {
        kernel = kernelFactory != null
            ? await kernelFactory()
            : await FluentDockerKernel.Create()
                .WithDockerCli("docker-cli", d => d.AsDefault())
                .BuildAsync();

        resource = resourceFactory(kernel)
            ?? throw new InvalidOperationException(
                "resourceFactory returned null. The factory must return a non-null resource.");
        await resource.InitializeAsync();

        Kernel = kernel;
        _resource = resource;
      }
      catch
      {
        try { if (resource != null) await resource.DisposeAsync(); } catch { /* best effort */ }
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
