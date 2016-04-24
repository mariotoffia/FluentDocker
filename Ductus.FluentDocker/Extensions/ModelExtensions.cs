using System;
using System.Linq;
using System.Text;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Extensions
{
  public static class ModelExtensions
  {
    public static void RenderIfExists(this StringBuilder sb, string option, string value)
    {
      if (!string.IsNullOrEmpty(value))
      {
        sb.Append($" {option}{value}");
      }
    }

    public static void RenderIfExists(this StringBuilder sb, string option, string[] values)
    {
      if (null == values || 0 == values.Length)
      {
        return;
      }

      foreach (var value in values)
      {
        sb.Append($" {option}{value}");
      }
    }

    /// <summary>
    ///   Strips the hash algorithm prefixed (if any) from the container hash id.
    /// </summary>
    /// <param name="hashAlgAndContainerHash">The hashalg:containerhash string.</param>
    /// <returns>A "raw" container id hash.</returns>
    public static string Strip(this string hashAlgAndContainerHash)
    {
      var split = hashAlgAndContainerHash.Split(':');
      return split.Length == 2 ? split[1] : hashAlgAndContainerHash;
    }

    public static string ToDockerString(this ContainerIsolationTechnology isolation)
    {
      switch (isolation)
      {
        case ContainerIsolationTechnology.Default:
          return "default";
        case ContainerIsolationTechnology.Hyperv:
          return "hyperv";
        case ContainerIsolationTechnology.Process:
          return "process";
        default:
          return null;
      }
    }

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

    public static string[] AddToArrayDistinct(this string[] arr, params string[] values)
    {
      return AddToArray(arr,values).Distinct().ToArray();
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