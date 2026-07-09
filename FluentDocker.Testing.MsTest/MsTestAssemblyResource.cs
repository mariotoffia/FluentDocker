using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;

namespace FluentDocker.Testing.MsTest
{
  /// <summary>
  /// Holder for one FluentDocker resource shared by an MSTest assembly.
  /// </summary>
  /// <remarks>
  /// <code>
  /// private static readonly MsTestAssemblyResource&lt;ContainerResource&gt; Shared = new();
  ///
  /// [AssemblyInitialize]
  /// public static Task AssemblyInitialize(TestContext _)
  ///     =&gt; Shared.InitializeAsync(k =&gt; new ContainerResource(k, b =&gt; b.UseImage("redis:7")));
  ///
  /// [AssemblyCleanup]
  /// public static async Task AssemblyCleanup()
  ///     =&gt; await Shared.DisposeAsync().ConfigureAwait(false);
  /// </code>
  /// </remarks>
  public sealed class MsTestAssemblyResource<TResource> : IAsyncDisposable
      where TResource : class, ITestResource
  {
    private TResource? _resource;
    private FluentDockerKernel? _kernel;

    public TResource Resource
    {
      get { EnsureInitialized(); return _resource!; }
    }

    public FluentDockerKernel Kernel
    {
      get { EnsureInitialized(); return _kernel!; }
    }

    public async Task InitializeAsync(
        Func<FluentDockerKernel, TResource> resourceFactory,
        Func<Task<FluentDockerKernel>>? kernelFactory = null,
        CancellationToken cancellationToken = default)
    {
      if (_resource != null)
        throw new InvalidOperationException(
            "Assembly resource is already initialized. Dispose before re-initializing.");

      var (kernel, resource) = await ResourceLifecycle.CreateAndInitializeAsync(
          resourceFactory, kernelFactory!, cancellationToken: cancellationToken)
          .ConfigureAwait(false);
      _kernel = kernel;
      _resource = resource;
    }

    public async ValueTask DisposeAsync()
    {
      await ResourceLifecycle.DisposeAsync(_resource, _kernel).ConfigureAwait(false);
      _resource = null;
      _kernel = null;
      GC.SuppressFinalize(this);
    }

    private void EnsureInitialized()
    {
      if (_resource == null)
        throw new InvalidOperationException(
            "Assembly resource has not been initialized. Call InitializeAsync from [AssemblyInitialize].");
    }
  }
}
