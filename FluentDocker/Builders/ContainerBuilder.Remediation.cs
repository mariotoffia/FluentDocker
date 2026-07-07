using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;

namespace FluentDocker.Builders
{
  internal sealed partial class ContainerBuilder
  {
    private static void ValidateNonNegative(long value, string parameterName)
    {
      if (value < 0)
        throw new ArgumentOutOfRangeException(parameterName, value, "Value must be non-negative.");
    }

    private static void ValidateEnvironmentName(string name, string message)
    {
      if (string.IsNullOrWhiteSpace(name))
        throw new FluentDockerException(message);
    }

    private static async Task StartReusedContainerAsync(
        Drivers.IContainerDriver driver,
        DriverContext context,
        string containerId,
        IContainerService service,
        CancellationToken cancellationToken)
    {
      try
      {
        await service.StartAsync(cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        var logTail = ex is OperationCanceledException
            ? null
            : await ReadLogTailAsync(driver, context, containerId, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(logTail))
          ex.Data["ContainerLogTail"] = logTail;
        if (ex.GetType() == typeof(FluentDockerException))
          throw new FluentDockerException(AppendLogTail(ex.Message, logTail), ex);
        throw;
      }
    }
  }
}
