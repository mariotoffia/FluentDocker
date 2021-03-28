using System;
using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
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

    /// <summary>
    /// Creates a `IHostService` based on a _URI_.
    /// </summary>
    /// <param name="uri">The _URI_ to the docker daemon.</param>
    /// <param name="name">An optional name. If none is specified the _URI_ is the name.</param>
    /// <param name="isNative">If the docker daemon is native or not. Default to true.</param>
    /// <param name="stopWhenDisposed">If it should be stopped when disposed, default to false.</param>
    /// <param name="isWindowsHost">If it is a docker daemon that controls windows containers or not. Default false.</param>
    /// <param name="certificatePath">
    /// Optional path to where certificates are located in order to do TLS communication with docker daemon. If not provided,
    /// it will try to get it from the environment _DOCKER_CERT_PATH_.
    /// </param>
    /// <returns>A newly created host service.</returns>
    public IHostService FromUri(
      DockerUri uri,
      string name = null,
      bool isNative = true,
      bool stopWhenDisposed = false,
      bool isWindowsHost = false,
      string certificatePath = null)
    {

      if (string.IsNullOrEmpty(certificatePath))
      {
        certificatePath = Environment.GetEnvironmentVariable(DockerHostService.DockerCertPath);
      }

      if (string.IsNullOrEmpty(name))
      {
        name = uri.ToString();
      }

      return new DockerHostService(
        name, isNative, stopWhenDisposed, uri.ToString(), certificatePath, isWindowsHost
      );
    }

    public IHostService FromMachineName(string name, bool isWindowsHost = false, bool throwIfNotStarted = false)
    {
      return new DockerHostService(name, false, isWindowsHost, throwIfNotStarted);
    }
  }
}
