using System;
using System.Threading;
using System.Threading.Tasks;
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
    /// Probes whether a Docker daemon is reachable, so tests can skip (rather than fail)
    /// when Docker is absent (e.g. on Docker-less CI agents). Any failure — daemon down,
    /// binary missing, internal timeout — returns <c>false</c>. Caller-requested
    /// cancellation is honored: if <paramref name="cancellationToken"/> is cancelled the
    /// resulting <see cref="OperationCanceledException"/> propagates rather than being
    /// reported as unavailable.
    /// </summary>
    /// <param name="kernelFactory">Optional factory for the probe kernel. When null, a
    /// default Docker CLI kernel (driver id "docker-cli") is built and disposed internally.</param>
    /// <param name="driverId">Driver id to resolve the system driver from. Defaults to "docker-cli".</param>
    /// <param name="cancellationToken">Cancellation token for the probe.</param>
    public static async Task<bool> IsAvailableAsync(
        Func<Task<FluentDockerKernel>> kernelFactory = null,
        string driverId = "docker-cli",
        CancellationToken cancellationToken = default)
    {
      FluentDockerKernel kernel = null;
      try
      {
        kernel = kernelFactory != null
            ? await kernelFactory().ConfigureAwait(false)
            : await ResourceLifecycle.CreateDefaultDockerKernelAsync().ConfigureAwait(false);
        var system = kernel.SysCtl<ISystemDriver>(driverId);
        var response = await system.PingAsync(
            new DriverContext(driverId),
            cancellationToken).ConfigureAwait(false);
        return response.Success;
      }
      catch (Exception ex) when (
          ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
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
