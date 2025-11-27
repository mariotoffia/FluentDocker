using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentDocker.Common;
using FluentDocker.Executors;
using FluentDocker.Executors.Parsers;
using FluentDocker.Extensions;
using FluentDocker.Extensions.Utils;
using FluentDocker.Model.Commands;
using FluentDocker.Model.Common;
using FluentDocker.Model.Compose;
using FluentDocker.Model.Containers;

namespace FluentDocker.Commands
{
  public static class Compose
  {
    /// <summary>
    /// Returns the appropriate binary and command string for Docker Compose operations,
    /// handling both V1 and V2 formats.
    /// </summary>
    private static (string binary, string command) GetComposeCommand(ComposeVersion version = Model.Compose.ComposeVersion.Unknown)
    {
      var resolver = new DockerBinariesResolver(SudoMechanism.None, null);
      var isV2 = resolver.IsDockerComposeV2Available;

      if (isV2)
      {
        if (version != Model.Compose.ComposeVersion.Unknown && version != Model.Compose.ComposeVersion.V2)
        {
          throw new FluentDockerException(
            $"Requested compose version {version} but only V2 is available. Use the overload that accepts ComposeVersion to specify the version.");
        }

        // For V2, we resolve 'docker' and add 'compose' as the first command
        return ("docker".ResolveBinary(), "compose");
      }
      else
      {
        if (version != Model.Compose.ComposeVersion.Unknown && version != Model.Compose.ComposeVersion.V1)
        {
          throw new FluentDockerException(
            $"Requested compose version {version} but only V1 is available. Use the overload that accepts ComposeVersion to specify the version.");
        }

        // For V1, we use the traditional docker-compose binary
        return ("docker-compose".ResolveBinary(), "");
      }
    }

    /// <summary>
    /// Builds or rebuilds services defined in the compose file.
    /// </summary>
    public static CommandResponse<IList<string>> ComposeBuildCommand(this DockerUri host, ComposeBuildCommandArgs args)
    {
      var composeFiles = args.ComposeFiles?.ToArray() ?? Array.Empty<string>();
      var cwd = WorkingDirectory(composeFiles);

      var (binary, command) = GetComposeCommand();

      var dockerArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var dockerComposeArgs = "";

      if (composeFiles.Length > 0)
        foreach (var cf in composeFiles)
          if (!string.IsNullOrEmpty(cf))
            dockerComposeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(args.AltProjectName))
        dockerComposeArgs += $" -p {args.AltProjectName}";

      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{dockerComposeArgs} build{options}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(args.Env).Execute();
    }

    /// <summary>
    /// Creates containers for services defined in the compose file.
    /// </summary>
    public static CommandResponse<IList<string>> ComposeCreateCommand(this DockerUri host, ComposeCreateCommandArgs args)
    {
      var composeFiles = args.ComposeFiles?.ToArray() ?? Array.Empty<string>();
      var cwd = WorkingDirectory(composeFiles);
      var (binary, command) = GetComposeCommand();
      var dockerArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var composeArgs = "";

      if (composeFiles.Length > 0)
        foreach (var cf in composeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(args.AltProjectName))
        composeArgs += $" -p {args.AltProjectName}";

      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} create{options}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(args.Env).Execute();
    }

    /// <summary>
    /// Starts services defined in the compose file.
    /// </summary>
    public static CommandResponse<IList<string>> ComposeStartCommand(this DockerUri host, ComposeStartCommandArgs args)
    {
      var composeFiles = args.ComposeFiles?.ToArray() ?? Array.Empty<string>();
      var cwd = WorkingDirectory(composeFiles);
      var (binary, command) = GetComposeCommand();
      var dockerArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var composeArgs = "";

      if (composeFiles.Length > 0)
        foreach (var cf in composeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(args.AltProjectName))
        composeArgs += $" -p {args.AltProjectName}";

      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} start{options}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(args.Env).Execute();
    }

    /// <summary>
    /// Kills running containers for services defined in the compose file.
    /// </summary>
    public static CommandResponse<IList<string>> ComposeKillCommand(this DockerUri host, ComposeKillCommandArgs args)
    {
      var composeFiles = args.ComposeFiles?.ToArray() ?? Array.Empty<string>();
      var cwd = WorkingDirectory(composeFiles);
      var (binary, command) = GetComposeCommand();
      var dockerArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var composeArgs = "";

      if (composeFiles.Length > 0)
        foreach (var cf in composeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(args.AltProjectName))
        composeArgs += $" -p {args.AltProjectName}";

      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} kill{options}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(args.Env).Execute();
    }

    /// <summary>
    /// Stops running containers for services defined in the compose file.
    /// </summary>
    public static CommandResponse<IList<string>> ComposeStopCommand(this DockerUri host, ComposeStopCommandArgs args)
    {
      var composeFiles = args.ComposeFiles?.ToArray() ?? Array.Empty<string>();
      var cwd = WorkingDirectory(composeFiles);
      var (binary, command) = GetComposeCommand();
      var dockerArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var composeArgs = "";

      if (composeFiles.Length > 0)
        foreach (var cf in composeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(args.AltProjectName))
        composeArgs += $" -p {args.AltProjectName}";

      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} stop{options}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(args.Env).Execute();
    }

    /// <summary>
    /// Pauses running containers for services defined in the compose file.
    /// </summary>
    public static CommandResponse<IList<string>> ComposePauseCommand(this DockerUri host, ComposePauseCommandArgs args)
    {
      var composeFiles = args.ComposeFiles?.ToArray() ?? Array.Empty<string>();
      var cwd = WorkingDirectory(composeFiles);
      var (binary, command) = GetComposeCommand();
      var dockerArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var composeArgs = "";

      if (composeFiles.Length > 0)
        foreach (var cf in composeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(args.AltProjectName))
        composeArgs += $" -p {args.AltProjectName}";

      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} pause{options}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(args.Env).Execute();
    }

    /// <summary>
    /// Unpauses running containers for services defined in the compose file.
    /// </summary>
    public static CommandResponse<IList<string>> ComposeUnpauseCommand(this DockerUri host, ComposeUnpauseCommandArgs args)
    {
      var composeFiles = args.ComposeFiles?.ToArray() ?? Array.Empty<string>();
      var cwd = WorkingDirectory(composeFiles);
      var (binary, command) = GetComposeCommand();
      var dockerArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var composeArgs = "";

      if (composeFiles.Length > 0)
        foreach (var cf in composeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(args.AltProjectName))
        composeArgs += $" -p {args.AltProjectName}";

      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} unpause{options}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(args.Env).Execute();
    }

    /// <summary>
    /// Scales services to specified number of instances.
    /// </summary>
    public static CommandResponse<IList<string>> ComposeScaleCommand(this DockerUri host, ComposeScaleCommandArgs args)
    {
      var composeFiles = args.ComposeFiles?.ToArray() ?? Array.Empty<string>();
      var cwd = WorkingDirectory(composeFiles);
      var (binary, command) = GetComposeCommand();
      var dockerArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var composeArgs = "";

      if (composeFiles.Length > 0)
        foreach (var cf in composeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(args.AltProjectName))
        composeArgs += $" -p {args.AltProjectName}";

      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} scale{options}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(args.Env).Execute();
    }

    /// <summary>
    /// Shows the Docker Compose version information.
    /// </summary>
    public static CommandResponse<IList<string>> ComposeVersionCommand(this DockerUri host, ComposeVersionCommandArgs args)
    {
      var composeFiles = args.ComposeFiles?.ToArray() ?? Array.Empty<string>();
      var cwd = WorkingDirectory(composeFiles);
      var (binary, command) = GetComposeCommand();
      var dockerArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var composeArgs = "";

      if (composeFiles.Length > 0)
        foreach (var cf in composeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";
      if (!string.IsNullOrEmpty(args.AltProjectName))
        composeArgs += $" -p {args.AltProjectName}";

      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} version{options}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(args.Env).Execute();
    }

    /// <summary>
    /// Restarts services defined in the compose file.
    /// </summary>
    public static CommandResponse<IList<string>> ComposeRestartCommand(this DockerUri host, ComposeRestartCommandArgs args)
    {
      var composeFiles = args.ComposeFiles?.ToArray() ?? Array.Empty<string>();
      var cwd = WorkingDirectory(composeFiles);
      var (binary, command) = GetComposeCommand();
      var dockerArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var composeArgs = "";

      if (composeFiles.Length > 0)
        foreach (var cf in composeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(args.AltProjectName))
        composeArgs += $" -p {args.AltProjectName}";

      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} restart{options}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(args.Env).Execute();
    }

    /// <summary>
    /// Prints the public port for a port binding.
    /// </summary>
    public static CommandResponse<IList<string>> ComposePortCommand(this DockerUri host, ComposePortCommandArgs args)
    {
      var composeFiles = args.ComposeFiles?.ToArray() ?? Array.Empty<string>();
      var cwd = WorkingDirectory(composeFiles);
      var (binary, command) = GetComposeCommand();
      var dockerArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var composeArgs = "";
      if (composeFiles.Length > 0)
        foreach (var cf in composeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(args.AltProjectName))
        composeArgs += $" -p {args.AltProjectName}";

      var options = args.ToString();
      var privatePort = args.PrivatePort ?? string.Empty;

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} port{options} {args.Service} {privatePort}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(args.Env).Execute();
    }

    /// <summary>
    /// Validates and view the Compose file.
    /// </summary>
    public static CommandResponse<IList<string>> ComposeConfigCommand(this DockerUri host, ComposeConfigCommandArgs args)
    {
      var composeFiles = args.ComposeFiles?.ToArray() ?? Array.Empty<string>();
      var cwd = WorkingDirectory(composeFiles);
      var (binary, command) = GetComposeCommand();
      var dockerArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var composeArgs = "";

      if (composeFiles.Length > 0)
        foreach (var cf in composeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(args.AltProjectName))
        composeArgs += $" -p {args.AltProjectName}";

      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} config{options}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(args.Env).Execute();
    }

    /// <summary>
    /// Stops containers and removes containers, networks, volumes, and images created by up.
    /// </summary>
    public static CommandResponse<IList<string>> ComposeDownCommand(this DockerUri host, ComposeDownCommandArgs args)
    {
      var composeFiles = args.ComposeFiles?.ToArray() ?? Array.Empty<string>();
      var cwd = WorkingDirectory(composeFiles);
      var (binary, command) = GetComposeCommand();
      var dockerArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var composeArgs = "";

      if (composeFiles.Length > 0)
        foreach (var cf in composeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(args.AltProjectName))
        composeArgs += $" -p {args.AltProjectName}";

      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} down{options}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(args.Env).Execute();
    }

    /// <summary>
    /// Arguments for docker compose up command.
    /// </summary>
    public struct ComposeUpCommandArgs
    {
      public string AltProjectName { get; set; }
      public bool ForceRecreate { get; set; }
      public bool NoRecreate { get; set; }
      public bool DontBuild { get; set; }
      public bool BuildBeforeCreate { get; set; }
      public TimeSpan? Timeout { get; set; }
      public bool RemoveOrphans { get; set; }
      public bool UseColor { get; set; }
      public bool NoStart { get; set; }
      public bool Wait { get; set; }
      public int? WaitTimeoutSeconds { get; set; }
      public IList<string> Services { get; set; }
      public IDictionary<string, string> Env { get; set; }
      public ICertificatePaths Certificates { get; set; }
      public IList<string> ComposeFiles { get; set; }
      public TemplateString ProjectDirectory { get; set; }
    }

    /// <summary>
    /// Creates and starts containers for services defined in the compose file.
    /// </summary>
    public static CommandResponse<IList<string>> ComposeUpCommand(this DockerUri host, ComposeUpCommandArgs ca)
    {
      if (ca.ForceRecreate && ca.NoRecreate)
      {
        throw new InvalidOperationException("ForceRecreate and NoRecreate are incompatible.");
      }

      var cwd = WorkingDirectory(ca.ComposeFiles?.ToArray() ?? Array.Empty<string>());
      var (binary, command) = GetComposeCommand();

      var dockerArgs = $"{host.RenderBaseArgs(ca.Certificates)}";
      var composeArgs = "";

      if (null != ca.ComposeFiles && 0 != ca.ComposeFiles.Count)
        foreach (var cf in ca.ComposeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(ca.AltProjectName))
        composeArgs += $" -p {ca.AltProjectName}";

      if (!string.IsNullOrEmpty(ca.ProjectDirectory))
      {
        composeArgs += $" --project-directory {ca.ProjectDirectory.Rendered}";
      }

      var options = ca.NoStart ? "--no-start" : "--detach";

      if (ca.ForceRecreate)
        options += " --force-recreate";

      if (ca.NoRecreate)
        options += " --no-recreate";

      if (ca.DontBuild)
        options += " --no-build";

      if (ca.BuildBeforeCreate)
        options += " --build";

      if (!ca.UseColor)
        options += " --no-color";

      if (ca.Wait)
        options += " --wait";

      if (ca.WaitTimeoutSeconds.HasValue)
        options += $" --wait-timeout {ca.WaitTimeoutSeconds.Value}";

      if (null != ca.Timeout)
        options += $" -t {Math.Round(ca.Timeout.Value.TotalSeconds, 0)}";

      if (ca.RemoveOrphans)
        options += " --remove-orphans";

      if (null != ca.Services && 0 != ca.Services.Count)
        options += " " + string.Join(" ", ca.Services);

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} up {options}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(ca.Env).Execute();
    }

    /// <summary>
    /// Removes stopped service containers.
    /// </summary>
    public static CommandResponse<IList<string>> ComposeRmCommand(this DockerUri host, ComposeRmCommandArgs args)
    {
      var composeFiles = args.ComposeFiles?.ToArray() ?? Array.Empty<string>();
      var cwd = WorkingDirectory(composeFiles);
      var (binary, command) = GetComposeCommand();
      var dockerArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var composeArgs = "";

      if (composeFiles.Length > 0)
        foreach (var cf in composeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(args.AltProjectName))
        composeArgs += $" -p {args.AltProjectName}";

      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} rm{options}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(args.Env).Execute();
    }

    /// <summary>
    /// Lists containers for services defined in the compose file.
    /// </summary>
    public static CommandResponse<IList<string>> ComposePsCommand(this DockerUri host, ComposePsCommandArgs args)
    {
      var composeFiles = args.ComposeFiles?.ToArray() ?? Array.Empty<string>();
      var cwd = WorkingDirectory(composeFiles);
      var (binary, command) = GetComposeCommand();
      var dockerArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var composeArgs = "";

      if (composeFiles.Length > 0)
        foreach (var cf in composeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(args.AltProjectName))
        composeArgs += $" -p {args.AltProjectName}";

      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} ps{options}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(args.Env).Execute();
    }

    /// <summary>
    /// Arguments for docker compose pull command.
    /// </summary>
    public struct ComposePullCommandArgs
    {
      public string AltProjectName { get; set; }
      public bool DownloadAllTagged { get; set; }
      public bool SkipImageVerification { get; set; }
      public IList<string> Services { get; set; }
      public IDictionary<string, string> Env { get; set; }
      public ICertificatePaths Certificates { get; set; }
      public IList<string> ComposeFiles { get; set; }
    }

    /// <summary>
    /// Pulls images for services defined in the compose file.
    /// </summary>
    public static CommandResponse<IList<string>> ComposePullCommand(this DockerUri host, ComposePullCommandArgs commandArgs)
    {
      var dockerArgs = $"{host.RenderBaseArgs(commandArgs.Certificates)}";
      var (binary, command) = GetComposeCommand();
      var cwd = WorkingDirectory(commandArgs.ComposeFiles?.ToArray());
      var composeArgs = "";

      if (null != commandArgs.ComposeFiles && 0 != commandArgs.ComposeFiles.Count)
        foreach (var cf in commandArgs.ComposeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(commandArgs.AltProjectName))
        composeArgs += $" -p {commandArgs.AltProjectName}";

      var options = string.Empty;
      if (commandArgs.DownloadAllTagged)
        options += " -a";

      if (commandArgs.SkipImageVerification)
        options += " --disable-content-trust=true";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} pull {options} {string.Join(" ", commandArgs.Services ?? Array.Empty<string>())}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(commandArgs.Env).Execute();
    }

    /// <summary>
    /// Executes a command in a running service container.
    /// </summary>
    public static CommandResponse<IList<string>> ComposeExecCommand(this DockerUri host, ComposeExecCommandArgs args)
    {
      var composeFiles = args.ComposeFiles?.ToArray() ?? Array.Empty<string>();
      var cwd = WorkingDirectory(composeFiles);
      var (binary, command) = GetComposeCommand();
      var dockerArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var composeArgs = "";

      if (composeFiles.Length > 0)
        foreach (var cf in composeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(args.AltProjectName))
        composeArgs += $" -p {args.AltProjectName}";

      var options = args.ToString();
      var cmdArgs = args.Arguments != null && args.Arguments.Count > 0
        ? " " + string.Join(" ", args.Arguments)
        : "";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} exec{options} {args.Service} {args.Command}{cmdArgs}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(args.Env).Execute();
    }

    /// <summary>
    /// Runs a one-off command in a service container.
    /// </summary>
    public static CommandResponse<IList<string>> ComposeRunCommand(this DockerUri host, ComposeRunCommandArgs args)
    {
      var composeFiles = args.ComposeFiles?.ToArray() ?? Array.Empty<string>();
      var cwd = WorkingDirectory(composeFiles);
      var (binary, command) = GetComposeCommand();
      var dockerArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var composeArgs = "";

      if (composeFiles.Length > 0)
        foreach (var cf in composeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(args.AltProjectName))
        composeArgs += $" -p {args.AltProjectName}";

      var options = args.ToString();
      var cmdWithArgs = string.IsNullOrEmpty(args.Command)
        ? ""
        : args.Arguments != null && args.Arguments.Count > 0
          ? $" {args.Command} {string.Join(" ", args.Arguments)}"
          : $" {args.Command}";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} run{options} {args.Service}{cmdWithArgs}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(args.Env).Execute();
    }

    /// <summary>
    /// Displays the running processes in a service container.
    /// </summary>
    public static CommandResponse<IList<string>> ComposeTopCommand(this DockerUri host, ComposeTopCommandArgs args)
    {
      var composeFiles = args.ComposeFiles?.ToArray() ?? Array.Empty<string>();
      var cwd = WorkingDirectory(composeFiles);
      var (binary, command) = GetComposeCommand();
      var dockerArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var composeArgs = "";

      if (composeFiles.Length > 0)
        foreach (var cf in composeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(args.AltProjectName))
        composeArgs += $" -p {args.AltProjectName}";

      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} top{options}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(args.Env).Execute();
    }

    /// <summary>
    /// Lists images used by the created containers.
    /// </summary>
    public static CommandResponse<IList<string>> ComposeImagesCommand(this DockerUri host, ComposeImagesCommandArgs args)
    {
      var composeFiles = args.ComposeFiles?.ToArray() ?? Array.Empty<string>();
      var cwd = WorkingDirectory(composeFiles);
      var (binary, command) = GetComposeCommand();
      var dockerArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var composeArgs = "";

      if (composeFiles.Length > 0)
        foreach (var cf in composeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(args.AltProjectName))
        composeArgs += $" -p {args.AltProjectName}";

      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} images{options}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(args.Env).Execute();
    }

    /// <summary>
    /// Copies files/folders between a service container and the local filesystem.
    /// </summary>
    public static CommandResponse<IList<string>> ComposeCpCommand(this DockerUri host, ComposeCpCommandArgs args)
    {
      var composeFiles = args.ComposeFiles?.ToArray() ?? Array.Empty<string>();
      var cwd = WorkingDirectory(composeFiles);
      var (binary, command) = GetComposeCommand();
      var dockerArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var composeArgs = "";

      if (composeFiles.Length > 0)
        foreach (var cf in composeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(args.AltProjectName))
        composeArgs += $" -p {args.AltProjectName}";

      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} cp{options} {args.Source} {args.Destination}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(args.Env).Execute();
    }

    /// <summary>
    /// Displays log output from services defined in the compose file (non-streaming).
    /// </summary>
    public static CommandResponse<IList<string>> ComposeLogsCommand(this DockerUri host, ComposeLogsCommandArgs args)
    {
      var composeFiles = args.ComposeFiles?.ToArray() ?? Array.Empty<string>();
      var cwd = WorkingDirectory(composeFiles);
      var (binary, command) = GetComposeCommand();
      var dockerArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var composeArgs = "";

      if (composeFiles.Length > 0)
        foreach (var cf in composeFiles)
          if (!string.IsNullOrEmpty(cf))
            composeArgs += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(args.AltProjectName))
        composeArgs += $" -p {args.AltProjectName}";

      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          binary,
          $"{dockerArgs} {(string.IsNullOrEmpty(command) ? "" : command + " ")}{composeArgs} logs{options}",
          cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(args.Env).Execute();
    }

    private static WorkingDirectoryInfo WorkingDirectory(params string[] composeFile)
    {
      var curr = Directory.GetCurrentDirectory();
      var cwd = curr;

      if (null == composeFile || 0 == composeFile.Length)
        return new WorkingDirectoryInfo { Curr = curr, Cwd = cwd };

      if (!string.IsNullOrEmpty(composeFile[0])) // First is assumed to be baseline
        cwd = Path.GetDirectoryName(Path.IsPathRooted(composeFile[0])
          ? composeFile[0]
          : Path.Combine(curr, composeFile[0]));

      return new WorkingDirectoryInfo { Curr = curr, Cwd = cwd };
    }

    private struct WorkingDirectoryInfo
    {
      public string Cwd { get; set; }
      public string Curr { get; set; }

      public bool NeedCwd => Cwd != Curr;
    }
  }
}
