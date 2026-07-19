using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Swarm service convergence waiting for the Docker API driver. Split into its own partial file
  /// purely to keep each source file within the repository's 500-line limit.
  /// </summary>
  public partial class DockerApiServiceDriver
  {
    /// <summary>
    /// Blocks until a service reaches <paramref name="desiredReplicas"/> running tasks, bounded by
    /// the request timeout. This gives <c>detach=false</c> Scale/Rollback the same "wait for
    /// convergence" semantics the CLI driver honors, instead of returning the moment the update POST
    /// is accepted — a silent cross-driver divergence behind the kernel-resolved port (DAPI-MAJ-1).
    /// </summary>
    private async Task<CommandResponse<Unit>> WaitForServiceConvergenceAsync(
        DriverContext context, string serviceId, int desiredReplicas, CancellationToken cancellationToken)
    {
      if (desiredReplicas <= 0)
        return CommandResponse<Unit>.Ok(Unit.Default);

      var timeout = context?.RequestTimeout ?? TimeSpan.FromMinutes(5);
      using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      timeoutCts.CancelAfter(timeout);

      try
      {
        while (true)
        {
          var tasks = await GetTasksAsync(
              context!, serviceId, new ServiceTaskFilter { DesiredState = "running" }, timeoutCts.Token)
              .ConfigureAwait(false);
          if (!tasks.Success)
            return CommandResponse<Unit>.Fail(tasks.Error ?? string.Empty, tasks.ErrorCode);

          var running = (tasks.Data ?? []).Count(t =>
              string.Equals(t.CurrentState, "running", StringComparison.OrdinalIgnoreCase));
          if (running >= desiredReplicas)
            return CommandResponse<Unit>.Ok(Unit.Default);

          await Task.Delay(TimeSpan.FromMilliseconds(500), timeoutCts.Token).ConfigureAwait(false);
        }
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw; // caller cancelled — propagate
      }
      catch (OperationCanceledException)
      {
        return CommandResponse<Unit>.Fail(
            $"Service '{serviceId}' did not converge to {desiredReplicas} running task(s) within " +
            $"{timeout.TotalSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}s.",
            ErrorCodes.Service.UpdateFailed,
            CreateErrorContext($"convergence wait for service {serviceId}", 0));
      }
    }
  }
}
