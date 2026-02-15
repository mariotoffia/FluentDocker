using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Testing.Core;

namespace FluentDocker.Testing.Xunit
{
  /// <summary>
  /// xUnit fixture that wraps a <see cref="ComposeResource"/>.
  /// Use with <c>IClassFixture&lt;XunitComposeFixture&gt;</c> or
  /// <c>ICollectionFixture&lt;XunitComposeFixture&gt;</c>.
  /// </summary>
  public class XunitComposeFixture : IAsyncDisposable
  {
    private ComposeResource _resource;

    /// <summary>
    /// The underlying compose resource.
    /// </summary>
    public ComposeResource Resource => _resource;

    /// <summary>
    /// The running compose service, available after initialization.
    /// </summary>
    public IComposeService Service => _resource?.Service;

    /// <summary>
    /// The kernel managing drivers.
    /// </summary>
    public FluentDockerKernel Kernel { get; private set; }

    /// <summary>
    /// Configures and initializes the fixture.
    /// </summary>
    public async Task InitializeAsync(
        Action<IComposeBuilder> configure,
        Func<Task<FluentDockerKernel>> kernelFactory = null,
        DockerResourceOptions options = null)
    {
      Kernel = kernelFactory != null
          ? await kernelFactory()
          : await FluentDockerKernel.Create()
              .WithDockerCli("docker-cli", d => d.AsDefault())
              .BuildAsync();

      _resource = new ComposeResource(Kernel, configure, options);
      await _resource.InitializeAsync();
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
