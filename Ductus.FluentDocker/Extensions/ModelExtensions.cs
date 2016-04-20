using Ductus.FluentDocker.Model;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Extensions
{
  public static class ModelExtensions
  {
    public static ServiceRunningState ToServiceState(this ContainerState state)
    {
      if (null == state)
      {
        return ServiceRunningState.Unknown;
      }

      if (state.Running)
      {
        return ServiceRunningState.Running;
      }

      if (state.Dead)
      {
        return ServiceRunningState.Stopped;
      }

      if (state.Restarting)
      {
        return ServiceRunningState.Starting;
      }

      if (state.Paused)
      {
        return ServiceRunningState.Paused;
      }

      var status = state.Status?.ToLower() ?? string.Empty;
      if (status == "created")
      {
        return ServiceRunningState.Stopped;
      }

      return ServiceRunningState.Unknown;
    }
  }
}
