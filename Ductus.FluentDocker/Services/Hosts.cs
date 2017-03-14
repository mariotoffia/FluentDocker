using System;
using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Services.Impl;

namespace Ductus.FluentDocker.Services
{
  public sealed class Hosts
  {
    public IList<IHostService> Discover()
    {
      var list = new List<IHostService>();

      var ls = Machine.Ls();
      if (!ls.Success)
      {
        return list;
      }

      list.AddRange(from machine in ls.Data
        let inspect = machine.Name.Inspect()
        select
          new DockerHostService(machine.Name, false, false, machine.Docker?.ToString(), inspect.Data.AuthConfig.CertDir));

      if (CommandExtensions.IsEmulatedNative() || CommandExtensions.IsNative())
      {
        list.Add(new DockerHostService("native", true, false,
          Environment.GetEnvironmentVariable(DockerHostService.DockerHost),
          Environment.GetEnvironmentVariable(DockerHostService.DockerCertPath)));
      }

      return list;
    }
  }
}