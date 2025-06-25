using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Executors.Parsers;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Images;

namespace Ductus.FluentDocker.Commands
{
  public static class Compose
  {
    public static CommandResponse<IList<string>> ComposeBuild(this DockerUri host,
      string altProjectName = null,
      bool forceRm = false, bool dontUseCache = false,
      bool alwaysPull = false, string[] services = null /*all*/,
      IDictionary<string, string> env = null,
      ICertificatePaths certificates = null,
      params string[] composeFile)
    {
      var cwd = WorkingDirectory(composeFile);

      var args = $"{host.RenderBaseArgs(certificates)}";

      if (null != composeFile && 0 != composeFile.Length)
        foreach (var cf in composeFile)
          if (!string.IsNullOrEmpty(cf))
            args += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(altProjectName))
        args += $" -p {altProjectName}";

      var options = string.Empty;
      if (forceRm)
        options += " --force-rm";

      if (alwaysPull)
        options += " --pull";

      if (forceRm)
        options += " --force-rm";

      if (null != services && 0 != services.Length)
        options += " " + string.Join(" ", services);

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} build {options}", cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(env).Execute();
    }

    public static CommandResponse<IList<string>> ComposeCreate(this DockerUri host,
      string altProjectName = null,
      bool forceRecreate = false, bool noRecreate = false, bool dontBuild = false,
      bool buildBeforeCreate = false,
      string[] services = null /*all*/,
      IDictionary<string, string> env = null,
      ICertificatePaths certificates = null, params string[] composeFile)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";

      if (null != composeFile && 0 != composeFile.Length)
        foreach (var cf in composeFile)
          if (!string.IsNullOrEmpty(cf))
            args += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(altProjectName))
        args += $" -p {altProjectName}";

      var options = string.Empty;
      if (forceRecreate)
        options += " --force-recreate";

      if (noRecreate)
        options += " --no-recreate";

      if (dontBuild)
        options += " --no-build";

      if (buildBeforeCreate)
        options += " --build";

      if (null != services && 0 != services.Length)
        options += " " + string.Join(" ", services);

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} create {options}", cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(env).Execute();
    }

    public static CommandResponse<IList<string>> ComposeStart(this DockerUri host, string altProjectName = null,
      string[] services = null /*all*/,
      IDictionary<string, string> env = null,
      ICertificatePaths certificates = null, params string[] composeFile)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";

      if (null != composeFile && 0 != composeFile.Length)
        foreach (var cf in composeFile)
          if (!string.IsNullOrEmpty(cf))
            args += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(altProjectName))
        args += $" -p {altProjectName}";

      var options = string.Empty;
      if (null != services && 0 != services.Length)
        options += " " + string.Join(" ", services);

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} start -d {options}", cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(env).Execute();
    }

    public static CommandResponse<IList<string>> ComposeKill(this DockerUri host, string altProjectName = null,
      UnixSignal signal = UnixSignal.SIGKILL, string[] services = null /*all*/,
      IDictionary<string, string> env = null,
      ICertificatePaths certificates = null, params string[] composeFile)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";

      if (null != composeFile && 0 != composeFile.Length)
        foreach (var cf in composeFile)
          if (!string.IsNullOrEmpty(cf))
            args += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(altProjectName))
        args += $" -p {altProjectName}";

      var options = string.Empty;
      if (UnixSignal.SIGKILL != signal)
        options += $" -s {signal}";

      if (null != services && 0 != services.Length)
        options += " " + string.Join(" ", services);

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} kill {options}", cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(env).Execute();
    }

    public static CommandResponse<IList<string>> ComposeStop(this DockerUri host, string altProjectName = null,
      TimeSpan? timeout = null, string[] services = null /*all*/,
      IDictionary<string, string> env = null,
      ICertificatePaths certificates = null, params string[] composeFile)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";

      if (null != composeFile && 0 != composeFile.Length)
        foreach (var cf in composeFile)
          if (!string.IsNullOrEmpty(cf))
            args += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(altProjectName))
        args += $" -p {altProjectName}";

      var options = string.Empty;
      if (null != timeout)
        options += $" -t {Math.Round(timeout.Value.TotalSeconds, 0)}";

      if (null != services && 0 != services.Length)
        options += " " + string.Join(" ", services);

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} stop {options}", cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(env).Execute();
    }

    public static CommandResponse<IList<string>> ComposePause(this DockerUri host, string altProjectName = null,
      string[] services = null /*all*/,
      IDictionary<string, string> env = null,
      ICertificatePaths certificates = null, params string[] composeFile)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";

      if (null != composeFile && 0 != composeFile.Length)
        foreach (var cf in composeFile)
          if (!string.IsNullOrEmpty(cf))
            args += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(altProjectName))
        args += $" -p {altProjectName}";

      var options = string.Empty;
      if (null != services && 0 != services.Length)
        options += " " + string.Join(" ", services);

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} pause {options}", cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(env).Execute();
    }

    public static CommandResponse<IList<string>> ComposeUnPause(this DockerUri host, string altProjectName = null,
      string[] services = null /*all*/,
      IDictionary<string, string> env = null,
      ICertificatePaths certificates = null, params string[] composeFile)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";

      if (null != composeFile && 0 != composeFile.Length)
        foreach (var cf in composeFile)
          if (!string.IsNullOrEmpty(cf))
            args += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(altProjectName))
        args += $" -p {altProjectName}";

      var options = string.Empty;
      if (null != services && 0 != services.Length)
        options += " " + string.Join(" ", services);

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} unpause {options}", cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(env).Execute();
    }

    public static CommandResponse<IList<string>> ComposeScale(this DockerUri host, string altProjectName = null,
      TimeSpan? shutdownTimeout = null,
      string[] serviceEqNumber = null /*all*/,
      IDictionary<string, string> env = null,
      ICertificatePaths certificates = null,
      params string[] composeFile)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";

      if (null != composeFile && 0 != composeFile.Length)
        foreach (var cf in composeFile)
          if (!string.IsNullOrEmpty(cf))
            args += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(altProjectName))
        args += $" -p {altProjectName}";

      var options = string.Empty;
      if (null != shutdownTimeout)
        options = $" -t {Math.Round(shutdownTimeout.Value.TotalSeconds, 0)}";

      var services = string.Empty;
      if (null != serviceEqNumber && 0 != serviceEqNumber.Length)
        services += " " + string.Join(" ", serviceEqNumber);

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} scale {options} {services}", cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(env).Execute();
    }

    public static CommandResponse<IList<string>> ComposeVersion(this DockerUri host, string altProjectName = null,
      bool shortVersion = false,
      IDictionary<string, string> env = null,
      ICertificatePaths certificates = null,
      params string[] composeFile)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";

      if (null != composeFile && 0 != composeFile.Length)
        foreach (var cf in composeFile)
          if (!string.IsNullOrEmpty(cf))
            args += $" -f \"{cf}\"";
      if (!string.IsNullOrEmpty(altProjectName))
        args += $" -p {altProjectName}";

      var options = string.Empty;
      if (shortVersion)
        options = "--short";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} version {options}", cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(env).Execute();
    }

    public static CommandResponse<IList<string>> ComposeRestart(this DockerUri host, string altProjectName = null,
      string[] composeFile = null, TimeSpan? timeout = null,
      IDictionary<string, string> env = null,
      ICertificatePaths certificates = null,
      params string[] containerId)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";

      if (null != composeFile && 0 != composeFile.Length)
        foreach (var cf in composeFile)
          if (!string.IsNullOrEmpty(cf))
            args += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(altProjectName))
        args += $" -p {altProjectName}";

      var ids = string.Empty;
      if (null != containerId && 0 != containerId.Length)
        ids += " " + string.Join(" ", containerId);

      var options = string.Empty;
      if (null != timeout)
        options = $" -t {Math.Round(timeout.Value.TotalSeconds, 0)}";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} restart {options} {ids}", cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(env).Execute();
    }

    public static CommandResponse<IList<string>> ComposePort(this DockerUri host, string containerId,
      string privatePortAndProto = null,
      string altProjectName = null,
      IDictionary<string, string> env = null,
      ICertificatePaths certificates = null, params string[] composeFile)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";
      if (null != composeFile && 0 != composeFile.Length)
        foreach (var cf in composeFile)
          if (!string.IsNullOrEmpty(cf))
            args += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(altProjectName))
        args += $" -p {altProjectName}";

      if (string.IsNullOrEmpty(privatePortAndProto))
        privatePortAndProto = string.Empty;

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} port {containerId} {privatePortAndProto}", cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(env).Execute();
    }

    public static CommandResponse<IList<string>> ComposeConfig(this DockerUri host, string altProjectName = null,
      bool quiet = true,
      bool outputServices = false,
      IDictionary<string, string> env = null,
      ICertificatePaths certificates = null, params string[] composeFile)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";

      if (null != composeFile && 0 != composeFile.Length)
        foreach (var cf in composeFile)
          if (!string.IsNullOrEmpty(cf))
            args += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(altProjectName))
        args += $" -p {altProjectName}";

      var options = string.Empty;
      if (quiet)
        options += " -q";

      if (outputServices)
        options += " --services";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} config {options}", cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(env).Execute();
    }

    public static CommandResponse<IList<string>> ComposeDown(this DockerUri host, string altProjectName = null,
      ImageRemovalOption removeImagesFrom = ImageRemovalOption.None,
      bool removeVolumes = false, bool removeOrphanContainers = false,
      IDictionary<string, string> env = null,
      ICertificatePaths certificates = null,
      params string[] composeFile)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";

      if (null != composeFile && 0 != composeFile.Length)
        foreach (var cf in composeFile)
          if (!string.IsNullOrEmpty(cf))
            args += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(altProjectName))
        args += $" -p {altProjectName}";

      var options = string.Empty;
      if (removeOrphanContainers)
        options += " --remove-orphans";

      if (removeVolumes)
        options += " -v";

      if (removeImagesFrom != ImageRemovalOption.None)
        options += removeImagesFrom == ImageRemovalOption.Local ? " --rmi local" : " --rmi all";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} down {options}", cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(env).Execute();
    }

    [Obsolete("Use ComposeUpCommand(...)")]
    public static CommandResponse<IList<string>> ComposeUp(this DockerUri host,
      string altProjectName = null,
      bool forceRecreate = false, bool noRecreate = false, bool dontBuild = false,
      bool buildBeforeCreate = false, TimeSpan? timeout = null,
      bool removeOrphans = false,
      bool useColor = false,
      bool noStart = false,
      string[] services = null /*all*/,
      IDictionary<string, string> env = null,
      ICertificatePaths certificates = null, params string[] composeFile)
    {
      return host.ComposeUpCommand(new ComposeUpCommandArgs
      {
        AltProjectName = altProjectName,
        ForceRecreate = forceRecreate,
        NoRecreate = noRecreate,
        DontBuild = dontBuild,
        BuildBeforeCreate = buildBeforeCreate,
        Timeout = timeout,
        RemoveOrphans = removeOrphans,
        UseColor = useColor,
        NoStart = noStart,
        Services = services,
        Env = env,
        Certificates = certificates,
        ComposeFiles = composeFile
      });
    }

    public struct ComposeUpCommandArgs
    {
      public string AltProjectName {get;set;}
      public bool ForceRecreate {get;set;}
      public bool NoRecreate {get;set;}
      public bool DontBuild {get;set;}
      public bool BuildBeforeCreate {get;set;}
      public TimeSpan? Timeout {get;set;}
      public bool RemoveOrphans {get;set;}
      public bool UseColor {get;set;}
      public bool NoStart {get;set;}
      public IList<string> Services {get;set;}
      public IDictionary<string, string> Env {get;set;}
      public ICertificatePaths Certificates {get;set;}
      public  IList<string> ComposeFiles {get;set;}
      public TemplateString ProjectDirectory {get;set;}
    }

    public static CommandResponse<IList<string>> ComposeUpCommand(this DockerUri host, ComposeUpCommandArgs ca)
    {
      if (ca.ForceRecreate && ca.NoRecreate)
      {
        throw new InvalidOperationException("ForceRecreate and NoRecreate are incompatible.");
      }

      var cwd = WorkingDirectory(ca.ComposeFiles.ToArray());

      var args = $"{host.RenderBaseArgs(ca.Certificates)}";

      if (null != ca.ComposeFiles && 0 != ca.ComposeFiles.Count)
        foreach (var cf in ca.ComposeFiles)
          if (!string.IsNullOrEmpty(cf))
            args += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(ca.AltProjectName))
        args += $" -p {ca.AltProjectName}";

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

      if (null != ca.Timeout)
        options += $" -t {Math.Round(ca.Timeout.Value.TotalSeconds, 0)}";

      if (ca.RemoveOrphans)
        options += " --remove-orphans";

      if (!string.IsNullOrEmpty(ca.ProjectDirectory)) {
        options += $" --project-directory {ca.ProjectDirectory.Rendered}";
      }

      if (null != ca.Services && 0 != ca.Services.Count)
        options += " " + string.Join(" ", ca.Services);

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} up {options}", cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(ca.Env).Execute();
    }

    public static CommandResponse<IList<string>> ComposeRm(this DockerUri host, string altProjectName = null,
      bool force = false,
      bool removeVolumes = false,
      string[] services = null /*all*/,
      IDictionary<string, string> env = null,
      ICertificatePaths certificates = null, params string[] composeFile)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";

      if (null != composeFile && 0 != composeFile.Length)
        foreach (var cf in composeFile)
          if (!string.IsNullOrEmpty(cf))
            args += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(altProjectName))
        args += $" -p {altProjectName}";

      var options = string.Empty;
      options += " -f"; // Don't ask to confirm removal

      if (force)
        options += " -s";

      if (removeVolumes)
        options += " -v";

      if (null != services && 0 != services.Length)
        options += " " + string.Join(" ", services);

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} rm {options}", cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(env).Execute();
    }

    public static CommandResponse<IList<string>> ComposePs(this DockerUri host, string altProjectName = null,
      string[] services = null,
      IDictionary<string, string> env = null,
      ICertificatePaths certificates = null, params string[] composeFile)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";

      if (null != composeFile && 0 != composeFile.Length)
        foreach (var cf in composeFile)
          if (!string.IsNullOrEmpty(cf))
            args += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(altProjectName))
        args += $" -p {altProjectName}";

      var options = string.Empty;
      if (null != services && 0 != services.Length)
        options += " " + string.Join(" ", services);

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} ps -q {options}", cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(env).Execute();
    }

    public struct ComposePullCommandArgs
    {
      public string AltProjectName {get; set;}
      public bool DownloadAllTagged { get; set; }
      public bool SkipImageVerification { get; set; }
      public IList<string> Services { get; set; }
      public IDictionary<string, string> Env { get; set; }
      public ICertificatePaths Certificates { get; set; }
      public IList<string> ComposeFiles { get; set; }
    }

    public static CommandResponse<IList<string>> ComposePull(this DockerUri host, ComposePullCommandArgs commandArgs)
    {
      var args = $"{host.RenderBaseArgs(commandArgs.Certificates)}";

      var cwd = WorkingDirectory(commandArgs.ComposeFiles?.ToArray());

      if (null != commandArgs.ComposeFiles && 0 != commandArgs.ComposeFiles.Count)

        foreach (var cf in commandArgs.ComposeFiles)
          if (!string.IsNullOrEmpty(cf))
            args += $" -f \"{cf}\"";

      if (!string.IsNullOrEmpty(commandArgs.AltProjectName))
        args += $" -p {commandArgs.AltProjectName}";

      var options = string.Empty;
      if (commandArgs.DownloadAllTagged)
        options += " -a";

      if (commandArgs.SkipImageVerification)
        options += " --disable-content-trust=true";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} pull {options} {string.Join(" ", commandArgs.Services)}", cwd.NeedCwd ? cwd.Cwd : null).ExecutionEnvironment(commandArgs.Env).Execute();
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
