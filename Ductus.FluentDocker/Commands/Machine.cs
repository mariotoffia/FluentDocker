using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Executors.Parsers;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Machines;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Commands
{
  public static class Machine
  {
    public static bool IsPresent()
    {
      return CommandExtensions.IsMachineBinaryPresent();
    }

    public static CommandResponse<IList<MachineLsResponse>> Ls()
    {
      return new ProcessExecutor<MachineLsResponseParser, IList<MachineLsResponse>>(
        "docker-machine".ResolveBinary(), "ls --format=\"{{.Name}};{{.State}};{{.URL}}\"").Execute();
    }

    public static CommandResponse<MachineConfiguration> Inspect(this string machine)
    {
      return
        new ProcessExecutor<MachineInspectResponseParser, MachineConfiguration>(
          "docker-machine".ResolveBinary(), $"inspect {machine}").Execute();
    }

    public static CommandResponse<string> Start(this string machine)
    {
      return
        new ProcessExecutor<MachineStartStopResponseParser, string>(
          "docker-machine".ResolveBinary(), $"start {machine}").Execute();
    }

    public static CommandResponse<string> Stop(this string machine)
    {
      return
        new ProcessExecutor<MachineStartStopResponseParser, string>(
          "docker-machine".ResolveBinary(), $"stop {machine}").Execute();
    }

    public static CommandResponse<IDictionary<string, string>> Environment(this string machine)
    {
      return
        new ProcessExecutor<MachineEnvResponseParser, IDictionary<string, string>>(
          "docker-machine".ResolveBinary(), $"env {machine}").Execute();
    }

    /// <summary>
    ///   Creates a machine by passing the raw options (see docker-machine --help).
    /// </summary>
    /// <param name="machine">The name of the machine</param>
    /// <param name="driver">The machine driver</param>
    /// <param name="options">The "raw" docker-machine options.</param>
    /// <returns>Creation log.</returns>
    public static CommandResponse<string> Create(this string machine, string driver, params string[] options)
    {
      var opts = options.Aggregate(string.Empty, (current, option) => current + $"{option} ");
      var args = string.IsNullOrEmpty(opts) ? $"create -d {driver} {machine}" : $"create -d {driver} {opts} {machine}";

      return
        new ProcessExecutor<MachineCreateResponseParser, string>(
          "docker-machine".ResolveBinary(),
          args).Execute();
    }

    public static CommandResponse<string> Create(this string machine, int memMb, int volumeMb, int cpuCnt,
      params string[] options)
    {
      return Create(machine, $"{CommandDefaults.MachineDriver}",
        $"--{CommandDefaults.MachineDriver}-memory \"{memMb}\"",
        $"--{CommandDefaults.MachineDriver}-disk-size \"{volumeMb}\"",
        $"--{CommandDefaults.MachineDriver}-cpu-count \"{cpuCnt}\"");
    }

    public static CommandResponse<string> Delete(this string machine, bool force)
    {
      var args = "rm -y " + (force ? "-f " : string.Empty) + machine;
      return
        new ProcessExecutor<MachineRmResponseParser, string>(
          "docker-machine".ResolveBinary(),
          args).Execute();
    }

    public static DockerUri Uri(this string machine)
    {
      var resp =
        new ProcessExecutor<SingleStringResponseParser, string>(
          "docker-machine".ResolveBinary(), $"url {machine}").Execute();

      return resp.Data.StartsWith("Host is not running") ? null : new DockerUri(resp.Data);
    }

    public static ServiceRunningState Status(this string machine)
    {
      var resp = new ProcessExecutor<SingleStringResponseParser, string>(
        "docker-machine".ResolveBinary(), $"status {machine}").Execute();

      if (!resp.Success)
        return ServiceRunningState.Unknown;

      switch (resp.Data)
      {
        case "Stopped":
          return ServiceRunningState.Stopped;
        case "Running":
          return ServiceRunningState.Running;
        default:
          return ServiceRunningState.Unknown;
      }
    }
  }
}