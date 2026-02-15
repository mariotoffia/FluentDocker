using System;
using System.Threading.Tasks;
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
    /// <typeparam name="TResource">The resource type.</typeparam>
    /// <param name="resourceFactory">Factory that receives a kernel and returns the resource.</param>
    /// <param name="kernelFactory">Optional kernel factory. When null, uses <paramref name="defaultKernelFactory"/>.</param>
    /// <param name="defaultKernelFactory">
    /// Fallback kernel factory when <paramref name="kernelFactory"/> is null.
    /// Defaults to <see cref="CreateDefaultDockerKernelAsync"/>.
    /// </param>
    public static async Task<(FluentDockerKernel kernel, TResource resource)>
        CreateAndInitializeAsync<TResource>(
            Func<FluentDockerKernel, TResource> resourceFactory,
            Func<Task<FluentDockerKernel>> kernelFactory = null,
            Func<Task<FluentDockerKernel>> defaultKernelFactory = null)
        where TResource : class, ITestResource
    {
      ArgumentNullException.ThrowIfNull(resourceFactory);
      defaultKernelFactory ??= CreateDefaultDockerKernelAsync;

      FluentDockerKernel kernel = null;
      TResource resource = null;
      try
      {
        kernel = kernelFactory != null
            ? await kernelFactory()
            : await defaultKernelFactory();

        resource = resourceFactory(kernel)
            ?? throw new InvalidOperationException(
                "resourceFactory returned null. The factory must return a non-null resource.");
        await resource.InitializeAsync();
        return (kernel, resource);
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

    /// <summary>
    /// Disposes a resource and its kernel in the correct order.
    /// </summary>
    public static async Task DisposeAsync(
        ITestResource resource, FluentDockerKernel kernel)
    {
      try
      {
        if (resource != null)
          await resource.DisposeAsync();
      }
      finally
      {
        kernel?.Dispose();
      }
    }
  }
}
