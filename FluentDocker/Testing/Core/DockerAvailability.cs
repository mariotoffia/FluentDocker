using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// Docker availability probes for test setup code.
  /// </summary>
  public static class DockerAvailability
  {
    /// <summary>
    /// Probes whether a Docker-compatible daemon is reachable, so tests can skip
    /// (rather than fail) when Docker or Podman is absent (e.g. on runtime-less CI
    /// agents). Availability failures — daemon down, binary missing, internal timeout
    /// — return <c>false</c>. Driver resolution/configuration failures propagate.
    /// Each call builds/probes/disposes a kernel; cache the result in your fixture
    /// when many tests share the same runtime.
    /// Caller-requested cancellation is honored: if <paramref name="cancellationToken"/>
    /// is cancelled the resulting <see cref="OperationCanceledException"/> propagates
    /// rather than being reported as unavailable.
    /// </summary>
    /// <param name="kernelFactory">Optional factory for the probe kernel. When null, a
    /// default Docker CLI kernel (driver id "docker-cli") is built and disposed internally.
    /// For Podman, pass <c>driverId: "podman-cli"</c> and a factory such as
    /// <see cref="ResourceLifecycle.CreateDefaultPodmanKernelAsync"/>.</param>
    /// <param name="driverId">Driver id to resolve the system driver from. When null
    /// (the default), the probe kernel's own default driver is used — "docker-cli" for
    /// the built-in Docker kernel, or the registered default of a custom
    /// <paramref name="kernelFactory"/> (e.g. "podman-cli").</param>
    /// <param name="cancellationToken">Cancellation token for the probe.</param>
    public static async Task<bool> IsAvailableAsync(
        Func<Task<FluentDockerKernel>>? kernelFactory = null,
        string? driverId = null,
        CancellationToken cancellationToken = default)
    {
      FluentDockerKernel? kernel = null;
      try
      {
        kernel = kernelFactory != null
            ? await kernelFactory().ConfigureAwait(false)
            : await ResourceLifecycle.CreateDefaultDockerKernelAsync().ConfigureAwait(false);
        driverId ??= kernel.DefaultDriverId;
        var system = kernel.SysCtl<ISystemDriver>(driverId);
        var response = await system.PingAsync(
            new DriverContext(driverId!),
            cancellationToken).ConfigureAwait(false);
        return response.Success;
      }
      catch (Exception ex) when (
          ex is not DriverNotFoundException &&
          ex is not InterfaceNotSupportedException &&
          (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested))
      {
        return false;
      }
      finally
      {
        if (kernel != null)
          await kernel.DisposeAsync().ConfigureAwait(false);
      }
    }
  }
}
