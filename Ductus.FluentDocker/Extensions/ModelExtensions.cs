using System;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Extensions
{
  public static class ModelExtensions
  {
    public static TemplateString AsTemplate(this string str)
    {
      return str;
    } 

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

    public static string ToDockerMountString(this MountType access)
    {
      switch (access)
      {
        case MountType.ReadOnly:
          return "ro";
        case MountType.ReadWrite:
          return "rw";
      }

      throw new NotImplementedException($"Not implemented type: {access}");
    }

    public static string[] AddToArray(this string[] arr, params string[] values)
    {
      if (null == values || 0 == values.Length)
      {
        return arr;
      }

      if (null == arr)
      {
        var ret = new string[values.Length];
        values.CopyTo(ret, 0);
        return ret;
      }

      var r = new string[arr.Length + values.Length];
      arr.CopyTo(r, 0);
      values.CopyTo(r, arr.Length);
      return r;
    }
  }
}