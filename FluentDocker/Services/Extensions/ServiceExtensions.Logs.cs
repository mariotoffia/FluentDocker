using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services.Impl;

namespace FluentDocker.Services.Extensions
{
  public static partial class ServiceExtensions
  {
    private static async Task<bool> ContainsLogMessageAsync(
        IContainerService service,
        string text,
        CancellationToken cancellationToken)
    {
      if (service is ContainerService containerService &&
          containerService.Kernel.TrySysCtl<IStreamDriver>(containerService.DriverId, out var streamDriver))
      {
        try
        {
          var context = new DriverContext(containerService.DriverId);
          var config = new StreamLogsConfig { Follow = false };
          await foreach (var line in streamDriver.StreamLogsAsync(
                  context, containerService.Id, config, cancellationToken)
              .WithCancellation(cancellationToken).ConfigureAwait(false))
          {
            if (line?.Contains(text) == true)
              return true;
          }

          return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
          throw;
        }
        catch (DriverException ex) when (ex.IsTransient)
        {
          return false;
        }
        catch (Exception ex) when (IsRetriableWaitException(ex))
        {
          LogDebug(service, ex, "WaitForLogMessageAsync", text);
          return false;
        }
      }

      try
      {
        var logs = await service.GetLogsAsync(false, cancellationToken).ConfigureAwait(false);
        return logs?.Contains(text) == true;
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (DriverException ex) when (ex.IsTransient)
      {
        return false;
      }
      catch (Exception ex) when (IsRetriableWaitException(ex))
      {
        LogDebug(service, ex, "WaitForLogMessageAsync", text);
        return false;
      }
    }
  }
}
