using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Kernel;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// Shared lifecycle utilities for creating, initializing, and disposing
  /// <see cref="ITestResource"/> instances with proper error handling.
  /// Used by framework-specific adapters (xUnit, MSTest, NUnit) to avoid
  /// duplicating the kernel-creation + resource-init + cleanup flow.
  /// </summary>
  public static class ResourceLifecycle
  {
    /// <summary>
    /// Creates a default Docker CLI kernel.
    /// </summary>
    public static Task<FluentDockerKernel> CreateDefaultDockerKernelAsync()
        => FluentDockerKernel.Create()
            .WithDockerCli("docker-cli", d => d.AsDefault())
            .BuildAsync();

    /// <summary>
    /// Creates a default Podman CLI kernel.
    /// </summary>
    public static Task<FluentDockerKernel> CreateDefaultPodmanKernelAsync()
        => FluentDockerKernel.Create()
            .WithPodmanCli("podman-cli", d => d.AsDefault())
            .BuildAsync();

    /// <summary>
    /// Creates a kernel, constructs a resource via factory, initializes it,
    /// and returns the pair. On failure, disposes the resource and kernel
    /// before re-throwing.
    /// </summary>
    /// <remarks>
    /// <para><b>Kernel ownership:</b> This method takes ownership of the kernel
    /// returned by the factory. Both the kernel and the resource will be disposed
    /// on failure. On success, the caller is responsible for disposing the returned
    /// kernel (typically via the adapter's <c>DisposeAsync</c>).</para>
    /// <para>The <paramref name="kernelFactory"/> must return a <b>new</b> kernel
    /// instance each time it is called. Do not return a shared or externally-managed
    /// kernel — it will be disposed when the resource is torn down.</para>
    /// </remarks>
    /// <typeparam name="TResource">The resource type.</typeparam>
    /// <param name="resourceFactory">Factory that receives a kernel and returns the resource.</param>
    /// <param name="kernelFactory">Optional kernel factory. When null, uses
    /// <paramref name="defaultKernelFactory"/>. Must return a new kernel per call.</param>
    /// <param name="defaultKernelFactory">
    /// Fallback kernel factory when <paramref name="kernelFactory"/> is null.
    /// Defaults to <see cref="CreateDefaultDockerKernelAsync"/>.
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token propagated to
    /// <see cref="ITestResource.InitializeAsync"/>.</param>
    public static async Task<(FluentDockerKernel kernel, TResource resource)>
        CreateAndInitializeAsync<TResource>(
            Func<FluentDockerKernel, TResource> resourceFactory,
            Func<Task<FluentDockerKernel>> kernelFactory = null,
            Func<Task<FluentDockerKernel>> defaultKernelFactory = null,
            CancellationToken cancellationToken = default)
        where TResource : class, ITestResource
    {
      ArgumentNullException.ThrowIfNull(resourceFactory);
      defaultKernelFactory ??= CreateDefaultDockerKernelAsync;

      FluentDockerKernel kernel = null;
      TResource resource = null;
      try
      {
        kernel = kernelFactory != null
            ? await kernelFactory().ConfigureAwait(false)
            : await defaultKernelFactory().ConfigureAwait(false);

        if (kernel == null)
          throw new InvalidOperationException(
              "Kernel factory returned null. The factory must return a non-null kernel.");

        resource = resourceFactory(kernel)
            ?? throw new InvalidOperationException(
                "resourceFactory returned null. The factory must return a non-null resource.");
        await resource.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return (kernel, resource);
      }
      catch (Exception ex)
      {
        Logger.Log($"Resource initialization failed: {ex.Message}");
        try
        { if (resource != null) await resource.DisposeAsync().ConfigureAwait(false); }
        catch (Exception resEx) { Logger.Log($"Resource cleanup failed: {resEx.Message}"); }
        try
        { if (kernel != null) await kernel.DisposeAsync().ConfigureAwait(false); }
        catch (Exception kernelEx) { Logger.Log($"Kernel cleanup failed: {kernelEx.Message}"); }
        throw;
      }
    }

    /// <summary>
    /// Disposes a resource and its kernel in the correct order.
    /// The resource is disposed first, then the kernel. Both are assumed
    /// to be owned by the caller (typically created via
    /// <see cref="CreateAndInitializeAsync{TResource}"/>).
    /// </summary>
    public static async Task DisposeAsync(
        ITestResource resource, FluentDockerKernel kernel)
    {
      Exception resourceFailure = null;
      try
      {
        if (resource != null)
          await resource.DisposeAsync().ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        resourceFailure = ex;
      }

      try
      {
        if (kernel != null)
          await kernel.DisposeAsync().ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        if (resourceFailure != null)
          throw new AggregateException(resourceFailure, ex);
        throw;
      }

      if (resourceFailure != null)
        ExceptionDispatchInfo.Capture(resourceFailure).Throw();
    }
  }
}
