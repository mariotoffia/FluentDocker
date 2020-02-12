using System;
using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Services.Impl;

namespace Ductus.FluentDocker.Services
{
  public sealed class Hosts
  {
    public IList<IHostService> Discover(bool preferNative = false)
    {
      var list = new List<IHostService>();

      var native = Native();
      if (null != native)
        list.Add(native);

      if (list.Count > 0 && preferNative)
        return list;

      if (!Machine.IsPresent())
        return list;

      try
      {
        var ls = Machine.Ls();
        if (ls.Success)
          list.AddRange(from machine in ls.Data select FromMachineName(machine.Name));
      }
      catch (FluentDockerException)
      {
        return list;
      }

      return list;
    }

    public IHostService Native()
    {
      if (CommandExtensions.IsEmulatedNative() || CommandExtensions.IsNative())
        return new DockerHostService("native", true, false, null,
          Environment.GetEnvironmentVariable(DockerHostService.DockerCertPath));

      return null;
    }

    public IHostService FromMachineName(string name, bool isWindowsHost = false, bool throwIfNotStarted = false)
    {
      return new DockerHostService(name, false, isWindowsHost, throwIfNotStarted);
    }
  }
}
