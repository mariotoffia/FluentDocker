using System.Collections.Generic;
using FluentDocker.Executors;
using FluentDocker.Executors.Parsers;
using FluentDocker.Extensions;
using FluentDocker.Model.Commands;
using FluentDocker.Model.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Volumes;

namespace FluentDocker.Commands
{
  public static class Volumes
  {
    #region New struct-based command methods

    /// <summary>
    /// Creates a volume using command args struct.
    /// </summary>
    public static CommandResponse<string> VolumeCreateCommand(this DockerUri host, VolumeCreateCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return
        new ProcessExecutor<SingleStringResponseParser, string>(
          "docker".ResolveBinary(),
          $"{certArgs} volume create {options}").Execute();
    }

    /// <summary>
    /// Lists volumes using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> VolumeLsCommand(this DockerUri host, VolumeLsCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} volume ls {options}").Execute();
    }

    /// <summary>
    /// Removes volumes using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> VolumeRmCommand(this DockerUri host, VolumeRmCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();
      var volumes = args.Volumes != null ? string.Join(" ", args.Volumes) : "";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} volume rm {options} {volumes}").Execute();
    }

    /// <summary>
    /// Inspects volumes using command args struct.
    /// </summary>
    public static CommandResponse<IList<Volume>> VolumeInspectCommand(this DockerUri host, VolumeInspectCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();
      var volumes = args.Volumes != null ? string.Join(" ", args.Volumes) : "";

      return
        new ProcessExecutor<VolumeInspectResponseParser, IList<Volume>>(
          "docker".ResolveBinary(),
          $"{certArgs} volume inspect {options} {volumes}").Execute();
    }

    /// <summary>
    /// Prunes unused volumes using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> VolumePruneCommand(this DockerUri host, VolumePruneCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} volume prune {options}").Execute();
    }

    #endregion

    #region Existing methods (backward compatible)
    public static CommandResponse<string> VolumeCreate(this DockerUri host, string name = null,
      string driver = null /*local*/, string[] labels = null, IDictionary<string, string> opts = null,
      ICertificatePaths certificates = null)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";

      var options = string.Empty;
      if (!string.IsNullOrEmpty(name))
        options += $" --name={name}";

      if (null != labels && labels.Length > 0)
        foreach (var label in labels)
          options += $" --label={label}";

      options += !string.IsNullOrEmpty(driver) ? $" --driver={driver}" : " --driver=local";
      if (null != opts && opts.Count > 0)
        foreach (var opt in opts)
          options += $" --opt={opt.Key}={opt.Value}";

      return
        new ProcessExecutor<SingleStringResponseParser, string>(
          "docker".ResolveBinary(),
          $"{args} volume create {options}").Execute();
    }

    public static CommandResponse<IList<Volume>> VolumeInspect(this DockerUri host,
      ICertificatePaths certificates = null, params string[] volume)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";

      var volumes = string.Join(" ", volume);

      return
        new ProcessExecutor<VolumeInspectResponseParser, IList<Volume>>(
          "docker".ResolveBinary(),
          $"{args} volume inspect {volumes}").Execute();
    }

    public static CommandResponse<IList<string>> VolumeLs(this DockerUri host, bool quiet = true,
      string format = null,
      ICertificatePaths certificates = null, params string[] filter)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";

      var options = string.Empty;
      if (quiet)
        options += " -q";

      if (null != filter && 0 != filter.Length)
        foreach (var f in filter)
          options += $" --filter={f}";

      if (!string.IsNullOrEmpty(format))
        options += $" --format \"{format}\"";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} volume ls {options}").Execute();
    }

    public static CommandResponse<IList<string>> VolumeRm(this DockerUri host,
      ICertificatePaths certificates = null, bool force = false, params string[] volume)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";
      var opts = string.Empty;

      if (force)
        opts = "-f";
      var volumes = string.Join(" ", volume);

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} volume rm {opts} {volumes}").Execute();
    }

    #endregion
  }
}
