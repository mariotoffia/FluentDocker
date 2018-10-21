using System;
using System.Collections.Generic;
using System.IO;
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
      string altProjectName = null, string composeFile = null,
      bool forceRm = false, bool dontUseCache = false,
      bool alwaysPull = false, string[] services = null /*all*/, ICertificatePaths certificates = null)
    {
      var cwd = WorkingDirectory(composeFile);
      
      var args = $"{host.RenderBaseArgs(certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f \"{composeFile}\"";
      }

      if (!string.IsNullOrEmpty(altProjectName))
      {
        args += $" -p {altProjectName}";
      }

      var options = string.Empty;
      if (forceRm)
      {
        options += " --force-rm";
      }

      if (alwaysPull)
      {
        options += " --pull";
      }

      if (forceRm)
      {
        options += " --force-rm";
      }

      if (null != services && 0 != services.Length)
      {
        options += " " + string.Join(" ", services);
      }

      try
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Cwd); // To pick up .env file
        
        return
          new ProcessExecutor<StringListResponseParser, IList<string>>(
            "docker-compose".ResolveBinary(),
            $"{args} build {options}", cwd.NeedCwd ? cwd.Cwd : null).Execute();
      }
      finally
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Curr);
      }
    }

    public static CommandResponse<IList<string>> ComposeCreate(this DockerUri host,
      string altProjectName = null, string composeFile = null,
      bool forceRecreate = false, bool noRecreate = false, bool dontBuild = false,
      bool buildBeforeCreate = false,
      string[] services = null /*all*/,
      ICertificatePaths certificates = null)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f \"{composeFile}\"";
      }

      if (!string.IsNullOrEmpty(altProjectName))
      {
        args += $" -p {altProjectName}";
      }

      var options = string.Empty;
      if (forceRecreate)
      {
        options += " --force-recreate";
      }

      if (noRecreate)
      {
        options += " --no-recreate";
      }

      if (dontBuild)
      {
        options += " --no-build";
      }

      if (buildBeforeCreate)
      {
        options += " --build";
      }

      if (null != services && 0 != services.Length)
      {
        options += " " + string.Join(" ", services);
      }

      try
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Cwd); // To pick up .env file
        
        return
          new ProcessExecutor<StringListResponseParser, IList<string>>(
            "docker-compose".ResolveBinary(),
            $"{args} create {options}", cwd.NeedCwd ? cwd.Cwd : null).Execute();
      }
      finally
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Curr);
      }
    }

    public static CommandResponse<IList<string>> ComposeStart(this DockerUri host, string altProjectName = null,
      string composeFile = null, string[] services = null /*all*/, ICertificatePaths certificates = null)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f \"{composeFile}\"";
      }

      if (!string.IsNullOrEmpty(altProjectName))
      {
        args += $" -p {altProjectName}";
      }

      var options = string.Empty;
      if (null != services && 0 != services.Length)
      {
        options += " " + string.Join(" ", services);
      }

      try
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Cwd); // To pick up .env file
        
        return
          new ProcessExecutor<StringListResponseParser, IList<string>>(
            "docker-compose".ResolveBinary(),
            $"{args} start -d {options}", cwd.NeedCwd ? cwd.Cwd : null).Execute();
      }
      finally
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Curr);
      }
    }

    public static CommandResponse<IList<string>> ComposeKill(this DockerUri host, string altProjectName = null,
      string composeFile = null, UnixSignal signal = UnixSignal.SIGKILL, string[] services = null /*all*/,
      ICertificatePaths certificates = null)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f \"{composeFile}\"";
      }

      if (!string.IsNullOrEmpty(altProjectName))
      {
        args += $" -p {altProjectName}";
      }

      var options = string.Empty;
      if (UnixSignal.SIGKILL != signal)
      {
        options += $" -s {signal}";
      }

      if (null != services && 0 != services.Length)
      {
        options += " " + string.Join(" ", services);
      }

      try
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Cwd); // To pick up .env file
        
        return
          new ProcessExecutor<StringListResponseParser, IList<string>>(
            "docker-compose".ResolveBinary(),
            $"{args} kill {options}", cwd.NeedCwd ? cwd.Cwd : null).Execute();
      }
      finally
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Curr);
      }
    }

    public static CommandResponse<IList<string>> ComposeStop(this DockerUri host, string altProjectName = null,
      string composeFile = null, TimeSpan? timeout = null, string[] services = null /*all*/,
      ICertificatePaths certificates = null)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f \"{composeFile}\"";
      }

      if (!string.IsNullOrEmpty(altProjectName))
      {
        args += $" -p {altProjectName}";
      }

      var options = string.Empty;
      if (null != timeout)
      {
        options += $" -t {Math.Round(timeout.Value.TotalSeconds, 0)}";
      }

      if (null != services && 0 != services.Length)
      {
        options += " " + string.Join(" ", services);
      }

      try
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Cwd); // To pick up .env file
        
        return
          new ProcessExecutor<StringListResponseParser, IList<string>>(
            "docker-compose".ResolveBinary(),
            $"{args} stop {options}", cwd.NeedCwd ? cwd.Cwd : null).Execute();
      }
      finally
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Curr);
      }
    }

    public static CommandResponse<IList<string>> ComposePause(this DockerUri host, string altProjectName = null,
      string composeFile = null, string[] services = null /*all*/, ICertificatePaths certificates = null)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f \"{composeFile}\"";
      }

      if (!string.IsNullOrEmpty(altProjectName))
      {
        args += $" -p {altProjectName}";
      }

      var options = string.Empty;
      if (null != services && 0 != services.Length)
      {
        options += " " + string.Join(" ", services);
      }

      try
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Cwd); // To pick up .env file
        
        return
          new ProcessExecutor<StringListResponseParser, IList<string>>(
            "docker-compose".ResolveBinary(),
            $"{args} pause {options}", cwd.NeedCwd ? cwd.Cwd : null).Execute();
      }
      finally
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Curr);
      }
    }

    public static CommandResponse<IList<string>> ComposeUnPause(this DockerUri host, string altProjectName = null,
      string composeFile = null, string[] services = null /*all*/, ICertificatePaths certificates = null)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f \"{composeFile}\"";
      }

      if (!string.IsNullOrEmpty(altProjectName))
      {
        args += $" -p {altProjectName}";
      }

      var options = string.Empty;
      if (null != services && 0 != services.Length)
      {
        options += " " + string.Join(" ", services);
      }

      try
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Cwd); // To pick up .env file
        
        return
          new ProcessExecutor<StringListResponseParser, IList<string>>(
            "docker-compose".ResolveBinary(),
            $"{args} unpause {options}", cwd.NeedCwd ? cwd.Cwd : null).Execute();
      }
      finally
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Curr);
      }
    }

    public static CommandResponse<IList<string>> ComposeScale(this DockerUri host, string altProjectName = null,
      string composeFile = null, TimeSpan? shutdownTimeout = null,
      string[] serviceEqNumber = null /*all*/, ICertificatePaths certificates = null)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f \"{composeFile}\"";
      }

      if (!string.IsNullOrEmpty(altProjectName))
      {
        args += $" -p {altProjectName}";
      }

      var options = string.Empty;
      if (null != shutdownTimeout)
      {
        options = $" -t {Math.Round(shutdownTimeout.Value.TotalSeconds, 0)}";
      }

      var services = string.Empty;
      if (null != serviceEqNumber && 0 != serviceEqNumber.Length)
      {
        services += " " + string.Join(" ", serviceEqNumber);
      }

      try
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Cwd); // To pick up .env file
        
        return
          new ProcessExecutor<StringListResponseParser, IList<string>>(
            "docker-compose".ResolveBinary(),
            $"{args} scale {options} {services}", cwd.NeedCwd ? cwd.Cwd : null).Execute();
      }
      finally
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Curr);
      }
    }

    public static CommandResponse<IList<string>> ComposeVersion(this DockerUri host, string altProjectName = null,
      string composeFile = null, bool shortVersion = false, ICertificatePaths certificates = null)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f \"{composeFile}\"";
      }

      if (!string.IsNullOrEmpty(altProjectName))
      {
        args += $" -p {altProjectName}";
      }

      var options = string.Empty;
      if (shortVersion)
      {
        options = "--short";
      }

      try
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Cwd); // To pick up .env file
        
        return
          new ProcessExecutor<StringListResponseParser, IList<string>>(
            "docker-compose".ResolveBinary(),
            $"{args} version {options}", cwd.NeedCwd ? cwd.Cwd : null).Execute();
      }
      finally
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Curr);
      }
    }

    public static CommandResponse<IList<string>> ComposeRestart(this DockerUri host, string altProjectName = null,
      string composeFile = null, TimeSpan? timeout = null, ICertificatePaths certificates = null,
      params string[] containerId)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f \"{composeFile}\"";
      }

      if (!string.IsNullOrEmpty(altProjectName))
      {
        args += $" -p {altProjectName}";
      }

      var ids = string.Empty;
      if (null != containerId && 0 != containerId.Length)
      {
        ids += " " + string.Join(" ", containerId);
      }

      var options = string.Empty;
      if (null != timeout)
      {
        options = $" -t {Math.Round(timeout.Value.TotalSeconds, 0)}";
      }

      try
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Cwd); // To pick up .env file
        
        return
          new ProcessExecutor<StringListResponseParser, IList<string>>(
            "docker-compose".ResolveBinary(),
            $"{args} restart {options} {ids}", cwd.NeedCwd ? cwd.Cwd : null).Execute();
      }
      finally
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Curr);
      }
    }

    public static CommandResponse<IList<string>> ComposePort(this DockerUri host, string containerId,
      string privatePortAndProto = null,
      string altProjectName = null, string composeFile = null,
      ICertificatePaths certificates = null)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f \"{composeFile}\"";
      }

      if (!string.IsNullOrEmpty(altProjectName))
      {
        args += $" -p {altProjectName}";
      }

      if (string.IsNullOrEmpty(privatePortAndProto))
      {
        privatePortAndProto = string.Empty;
      }

      try
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Cwd); // To pick up .env file
        
        return
          new ProcessExecutor<StringListResponseParser, IList<string>>(
            "docker-compose".ResolveBinary(),
            $"{args} port {containerId} {privatePortAndProto}", cwd.NeedCwd ? cwd.Cwd : null).Execute();
      }
      finally
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Curr);
      }
    }

    public static CommandResponse<IList<string>> ComposeConfig(this DockerUri host, string altProjectName = null,
      string composeFile = null, bool quiet = true,
      bool outputServices = false, ICertificatePaths certificates = null)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f \"{composeFile}\"";
      }

      if (!string.IsNullOrEmpty(altProjectName))
      {
        args += $" -p {altProjectName}";
      }

      var options = string.Empty;
      if (quiet)
      {
        options += " -q";
      }

      if (outputServices)
      {
        options += " --services";
      }

      try
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Cwd); // To pick up .env file
        
        return
          new ProcessExecutor<StringListResponseParser, IList<string>>(
            "docker-compose".ResolveBinary(),
            $"{args} config {options}", cwd.NeedCwd ? cwd.Cwd : null).Execute();
      }
      finally
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Curr);
      }
    }

    public static CommandResponse<IList<string>> ComposeDown(this DockerUri host, string altProjectName = null,
      string composeFile = null, ImageRemovalOption removeImagesFrom = ImageRemovalOption.None,
      bool removeVolumes = false, bool removeOrphanContainers = false, ICertificatePaths certificates = null)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f \"{composeFile}\"";
      }

      if (!string.IsNullOrEmpty(altProjectName))
      {
        args += $" -p {altProjectName}";
      }

      var options = string.Empty;
      if (removeOrphanContainers)
      {
        options += " --remove-orphans";
      }

      if (removeVolumes)
      {
        options += " -v";
      }

      if (removeImagesFrom != ImageRemovalOption.None)
      {
        options += removeImagesFrom == ImageRemovalOption.Local ? " --rmi local" : " --rmi all";
      }

      try
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Cwd); // To pick up .env file
        
        return
          new ProcessExecutor<StringListResponseParser, IList<string>>(
            "docker-compose".ResolveBinary(),
            $"{args} down {options}", cwd.NeedCwd ? cwd.Cwd : null).Execute();
      }
      finally
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Curr);
      }
    }

    public static CommandResponse<IList<string>> ComposeUp(this DockerUri host,
      string altProjectName = null, string composeFile = null,
      bool forceRecreate = false, bool noRecreate = false, bool dontBuild = false,
      bool buildBeforeCreate = false, TimeSpan? timeout = null,
      bool removeOphans = false,
      bool useColor = false,
      string[] services = null /*all*/,
      ICertificatePaths certificates = null)
    {
      var cwd = WorkingDirectory(composeFile);
      
      var args = $"{host.RenderBaseArgs(certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f \"{composeFile}\"";
      }

      if (!string.IsNullOrEmpty(altProjectName))
      {
        args += $" -p {altProjectName}";
      }

      // Always run in detached mode
      var options = "--detach";

      if (forceRecreate)
      {
        options += " --force-recreate";
      }

      if (noRecreate)
      {
        options += " --no-recreate";
      }

      if (dontBuild)
      {
        options += " --no-build";
      }

      if (buildBeforeCreate)
      {
        options += " --build";
      }

      if (!useColor)
      {
        options += " --no-color";
      }

      if (null != timeout)
      {
        options += $" -t {Math.Round(timeout.Value.TotalSeconds, 0)}";
      }

      if (removeOphans)
      {
        options += " --remove-orphans";
      }

      if (null != services && 0 != services.Length)
      {
        options += " " + string.Join(" ", services);
      }

      try
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Cwd); // To pick up .env file
        
        return
          new ProcessExecutor<StringListResponseParser, IList<string>>(
            "docker-compose".ResolveBinary(),
            $"{args} up {options}", cwd.NeedCwd ? cwd.Cwd : null).Execute();
      }
      finally
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Curr);
      }
    }

    public static CommandResponse<IList<string>> ComposeRm(this DockerUri host, string altProjectName = null,
      string composeFile = null, bool force = false,
      bool removeVolumes = false, bool all = false,
      string[] services = null /*all*/, ICertificatePaths certificates = null)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f \"{composeFile}\"";
      }

      if (!string.IsNullOrEmpty(altProjectName))
      {
        args += $" -p {altProjectName}";
      }

      var options = string.Empty;
      if (force)
      {
        options += " -f";
      }

      if (removeVolumes)
      {
        options += " -v";
      }

      if (all)
      {
        options += " -a";
      }

      if (null != services && 0 != services.Length)
      {
        options += " " + string.Join(" ", services);
      }

      try
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Cwd); // To pick up .env file
        
        return
          new ProcessExecutor<StringListResponseParser, IList<string>>(
            "docker-compose".ResolveBinary(),
            $"{args} rm {options}", cwd.NeedCwd ? cwd.Cwd : null).Execute();
      }
      finally
      {
        if (cwd.NeedCwd) Directory.SetCurrentDirectory(cwd.Curr);
      }
    }

    public static CommandResponse<IList<string>> ComposePs(this DockerUri host, string altProjectName = null,
      string composeFile = null, string[] services = null,
      ICertificatePaths certificates = null)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f \"{composeFile}\"";
      }

      if (!string.IsNullOrEmpty(altProjectName))
      {
        args += $" -p {altProjectName}";
      }

      var options = string.Empty;
      if (null != services && 0 != services.Length)
      {
        options += " " + string.Join(" ", services);
      }

        return
          new ProcessExecutor<StringListResponseParser, IList<string>>(
            "docker-compose".ResolveBinary(),
            $"{args} ps -q {options}", cwd.NeedCwd ? cwd.Cwd : null).Execute();
    }

    public static CommandResponse<IList<string>> ComposePull(this DockerUri host, string image,
      string altProjectName = null,
      string composeFile = null, bool downloadAllTagged = false, bool skipImageverficiation = false,
      ICertificatePaths certificates = null)
    {
      var cwd = WorkingDirectory(composeFile);
      var args = $"{host.RenderBaseArgs(certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f \"{composeFile}\"";
      }

      if (!string.IsNullOrEmpty(altProjectName))
      {
        args += $" -p {altProjectName}";
      }

      var options = string.Empty;
      if (downloadAllTagged)
      {
        options += " -a";
      }

      if (skipImageverficiation)
      {
        options += " --disable-content-trust=true";
      }

        return
          new ProcessExecutor<StringListResponseParser, IList<string>>(
            "docker-compose".ResolveBinary(),
            $"{args} pull {options} {image}", cwd.NeedCwd ? cwd.Cwd : null).Execute();
    }
    
    private static WorkingDirectoryInfo WorkingDirectory(string composeFile)
    {
      var curr = Directory.GetCurrentDirectory();
      var cwd = curr;
      if (!string.IsNullOrEmpty(composeFile))
      {
        cwd = Path.GetDirectoryName(Path.IsPathRooted(composeFile)
          ? composeFile
          : Path.Combine(curr, composeFile));
      }

      return new WorkingDirectoryInfo {Curr = curr, Cwd = cwd};
    }

    private struct WorkingDirectoryInfo
    {
      public string Cwd {get; set; }
      public string Curr { get; set; }

      public bool NeedCwd => Cwd != Curr;
    }
  }
}
