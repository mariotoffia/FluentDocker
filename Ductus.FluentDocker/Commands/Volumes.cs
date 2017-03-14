using System.Collections.Generic;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Executors.Parsers;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Commands
{
  public static class Volumes
  {
    public static CommandResponse<string> VolumeCreate(this DockerUri host, string name = null,
      string driver = null /*local*/, string[] labels = null, IDictionary<string, string> opts = null,
      ICertificatePaths certificates = null)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";

      var options = string.Empty;
      if (!string.IsNullOrEmpty(name))
      {
        options += $" --name={name}";
      }

      if (null != labels && labels.Length > 0)
      {
        foreach (var label in labels)
        {
          options += $" --label={label}";
        }
      }

      options += !string.IsNullOrEmpty(driver) ? $" --driver={driver}" : " --driver=local";
      if (null != opts && opts.Count > 0)
      {
        foreach (var opt in opts)
        {
          options += $" --opt={opt.Key}={opt.Value}";
        }
      }

      return
        new ProcessExecutor<SingleStringResponseParser, string>(
          "docker".ResolveBinary(),
          $"{args} volumes create {options}").Execute();
    }

    public static CommandResponse<IList<string>> VolumeInspect(this DockerUri host,
      ICertificatePaths certificates = null, params string[] volume)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";

      var volumes = string.Join(" ", volume);

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} volumes inspect {volumes}").Execute();
    }

    public static CommandResponse<string> VolumeLs(this DockerUri host, string[] filter = null, bool quiet = false,
      ICertificatePaths certificates = null, params string[] volume)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";

      var options = string.Empty;
      if (quiet)
      {
        options += " -q";
      }

      if (null != filter && 0 != filter.Length)
      {
        foreach (var f in filter)
        {
          options += $" --filter={f}";
        }
      }

      options += " " + string.Join(" ", volume);

      return
        new ProcessExecutor<SingleStringResponseParser, string>(
          "docker".ResolveBinary(),
          $"{args} volumes ls {options}").Execute();
    }

    public static CommandResponse<IList<string>> VolumeRm(this DockerUri host,
      ICertificatePaths certificates = null, params string[] volume)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";

      var volumes = string.Join(" ", volume);

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} volumes rm {volumes}").Execute();
    }
  }
}