using System;
using System.Collections.Generic;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Executors.Parsers;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Images;

namespace Ductus.FluentDocker.Commands
{
  public static class Compose
  {
    public static CommandResponse<IList<string>> Build(this Uri host,
      string altProjectName = null, string composeFile = null,
      bool forceRm = false, bool dontUseCache = false,
      bool alwaysPull = false, ICertificatePaths certificates = null)
    {
      var args = $"{Client.RenderBaseArgs(host, certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f {composeFile}";
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

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} build {options}").Execute();
    }

    public static CommandResponse<IList<string>> Create(this Uri host,
      string altProjectName = null, string composeFile = null,
      bool forceRecreate = false, bool noRecreate = false, bool dontBuild = false,
      bool buildBeforeCreate = false,
      ICertificatePaths certificates = null)
    {
      var args = $"{Client.RenderBaseArgs(host, certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f {composeFile}";
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

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} create {options}").Execute();
    }

    public static CommandResponse<IList<string>> Start(this Uri host, string altProjectName = null,
      string composeFile = null, ICertificatePaths certificates = null)
    {
      var args = $"{Client.RenderBaseArgs(host, certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f {composeFile}";
      }

      if (!string.IsNullOrEmpty(altProjectName))
      {
        args += $" -p {altProjectName}";
      }

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} start -d").Execute();
    }

    public static CommandResponse<IList<string>> Stop(this Uri host, string altProjectName = null,
      string composeFile = null, TimeSpan? timeout = null, ICertificatePaths certificates = null)
    {
      var args = $"{Client.RenderBaseArgs(host, certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f {composeFile}";
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

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} stop {options}").Execute();
    }

    public static CommandResponse<IList<string>> Config(this Uri host, string altProjectName = null,
      string composeFile = null, bool quiet = true,
      bool outputServices = false, ICertificatePaths certificates = null)
    {
      var args = $"{Client.RenderBaseArgs(host, certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f {composeFile}";
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

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} config {options}").Execute();
    }

    public static CommandResponse<IList<string>> Down(this Uri host, string altProjectName = null,
      string composeFile = null, ImageRemovalOption removeImagesFrom = ImageRemovalOption.None,
      bool removeVolumes = false, bool removeOrphanContainers = false, ICertificatePaths certificates = null)
    {
      var args = $"{Client.RenderBaseArgs(host, certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f {composeFile}";
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
        options += removeImagesFrom == ImageRemovalOption.Local ? " --rmi local" : " --rmi type all";
      }

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} down {options}").Execute();
    }

    public static CommandResponse<IList<string>> Up(this Uri host,
      string altProjectName = null, string composeFile = null,
      bool forceRecreate = false, bool noRecreate = false, bool dontBuild = false,
      bool buildBeforeCreate = false, TimeSpan? timeout = null,
      bool removeOphans = false,
      ICertificatePaths certificates = null)
    {
      var args = $"{Client.RenderBaseArgs(host, certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f {composeFile}";
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

      if (null != timeout)
      {
        options += $" -t {Math.Round(timeout.Value.TotalSeconds, 0)}";
      }

      if (removeOphans)
      {
        options += " --remove-orphans";
      }

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} up {options}").Execute();
    }

    public static CommandResponse<IList<string>> Rm(this Uri host, string altProjectName = null,
      string composeFile = null, bool force = false,
      bool removeVolumes = false, bool all = false, ICertificatePaths certificates = null)
    {
      var args = $"{Client.RenderBaseArgs(host, certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f {composeFile}";
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

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} rm {options}").Execute();
    }

    public static CommandResponse<IList<string>> Ps(this Uri host, string altProjectName = null,
      string composeFile = null, ICertificatePaths certificates = null)
    {
      var args = $"{Client.RenderBaseArgs(host, certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f {composeFile}";
      }

      if (!string.IsNullOrEmpty(altProjectName))
      {
        args += $" -p {altProjectName}";
      }

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker-compose".ResolveBinary(),
          $"{args} ps -q").Execute();
    }
  }
}