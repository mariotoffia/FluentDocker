using System;
using System.Threading;
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
  /// <remarks>
  /// <para><b>Prefer <see cref="XunitComposeFixtureBase"/></b> for most use cases.
  /// Subclass it and override <see cref="XunitComposeFixtureBase.ConfigureCompose"/>
  /// — xUnit handles the async lifecycle automatically.</para>
  /// <para>Use this class only when you need programmatic control over
  /// initialization (e.g., dynamic configuration, conditional setup).
  /// You <b>must</b> call <see cref="InitializeAsync"/> before accessing
  /// any properties — accessing them before initialization throws
  /// <see cref="InvalidOperationException"/>.</para>
  /// </remarks>
  public class XunitComposeFixture : IAsyncDisposable
  {
    private ComposeResource _resource;
    private FluentDockerKernel _kernel;

    /// <summary>
    /// The underlying compose resource.
    /// </summary>
    public ComposeResource Resource
    {
      get { EnsureInitialized(); return _resource; }
    }

    /// <summary>
    /// The running compose service, available after initialization.
    /// </summary>
    public IComposeService Service
    {
      get { EnsureInitialized(); return _resource.Service; }
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
    /// </summary>
    public async Task InitializeAsync(
        Action<IComposeBuilder> configure,
        Func<Task<FluentDockerKernel>> kernelFactory = null,
        DockerResourceOptions options = null,
        CancellationToken cancellationToken = default)
    {
      if (_resource != null)
        throw new InvalidOperationException(
            "Fixture has already been initialized. Dispose before re-initializing.");

      var (kernel, resource) = await ResourceLifecycle.CreateAndInitializeAsync(
          k => new ComposeResource(k, configure, options),
          kernelFactory,
          cancellationToken: cancellationToken);

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
