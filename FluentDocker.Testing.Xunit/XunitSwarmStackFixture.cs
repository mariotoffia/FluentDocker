using System;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;

namespace FluentDocker.Testing.Xunit
{
  /// <summary>
  /// xUnit fixture that wraps a <see cref="SwarmStackResource"/>.
  /// Use with <c>IClassFixture&lt;XunitSwarmStackFixture&gt;</c> or
  /// <c>ICollectionFixture&lt;XunitSwarmStackFixture&gt;</c>.
  /// </summary>
  public class XunitSwarmStackFixture : IAsyncDisposable
  {
    private SwarmStackResource _resource;

    /// <summary>
    /// The underlying swarm stack resource.
    /// </summary>
    public SwarmStackResource Resource => _resource;

    /// <summary>
    /// The stack name used for deployment.
    /// </summary>
    public string StackName => _resource?.StackName;

    /// <summary>
    /// The kernel managing drivers.
    /// </summary>
    public FluentDockerKernel Kernel { get; private set; }

    /// <summary>
    /// Configures and initializes the fixture.
    /// Call this from your fixture constructor or a setup method.
    /// </summary>
    /// <param name="config">Stack deploy configuration.</param>
    /// <param name="kernelFactory">Optional kernel factory. Defaults to Docker CLI.</param>
    /// <param name="options">Optional resource options.</param>
    public async Task InitializeAsync(
        StackDeployConfig config,
        Func<Task<FluentDockerKernel>> kernelFactory = null,
        DockerResourceOptions options = null)
    {
      if (_resource != null)
        throw new InvalidOperationException(
            "Fixture has already been initialized. Dispose before re-initializing.");

      FluentDockerKernel kernel = null;
      SwarmStackResource resource = null;
      try
      {
        kernel = kernelFactory != null
            ? await kernelFactory()
            : await FluentDockerKernel.Create()
                .WithDockerCli("docker-cli", d => d.AsDefault())
                .BuildAsync();

        resource = new SwarmStackResource(kernel, config, options);
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
