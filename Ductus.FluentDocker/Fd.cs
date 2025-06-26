using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Impl;

namespace Ductus.FluentDocker
{
  public static class Fd
  {
    public static void Run<T>(this IBuilder builder, Action<T> run, string name = null) where T : IService
    {
      try
      {
        using (var service = builder.Build())
        {
          service.Start();
          run.Invoke((T)service);
        }
      }
      catch
      {
        if (null != name)
          Logger.Log($"Failed to run service {name}");
        throw;
      }
    }

    public static void Run<T>(Func<Builder, IBuilder> builder, Action<T> run, string name = null) where T : IService
    {
      try
      {
        using (var service = builder.Invoke(Build()).Build())
        {
          service.Start();
          run.Invoke((T)service);
        }
      }
      catch
      {
        if (null != name)
          Logger.Log($"Failed to run service {name}");
        throw;
      }
    }

    public static void Container(this IBuilder builder, Action<IContainerService> run, string name = null)
    {
      Run(builder, run, name);
    }

    public static void Container(Func<Builder, IBuilder> builder, Action<IContainerService> run, string name = null)
    {
      Run(builder, run, name);
    }

    public static void Composite(this IBuilder builder, Action<ICompositeService> run, string name = null)
    {
      Run(builder, run, name);
    }

    public static void Composite(Func<Builder, IBuilder> builder, Action<ICompositeService> run, string name = null)
    {
      Run(builder, run, name);
    }

    internal static void DisposeOnException<T>(Action<T> action, T service, string name = null) where T : IService
    {
      if (null == name)
        name = "n/a";

      try
      {
        action.Invoke(service);
      }
      catch
      {
        Logger.Log($"Failed to run action for {name} disposing service {service.Name}");
        service.Dispose();
        throw;
      }
    }

    #region Build Support
    public static Builder Build()
    {
      return new Builder();
    }

    public static ContainerBuilder UseContainer()
    {
      return new Builder().UseContainer();
    }

    public static HostBuilder UseHost()
    {
      return new Builder().UseHost();
    }

    public static ImageBuilder DefineImage(string image)
    {
      return new Builder().DefineImage(image);
    }

    /// <summary>
    /// Creates a in-memory Dockerfile builder.
    /// </summary>
    /// <returns>A builder to build a Dockerfile</returns>
    /// <remarks>
    ///   This builder won't build an Image, use <see cref="DefineImage(string)"/>
    ///   for that purpose. This is a builder that can produce a string representing
    ///   a docker file.
    /// </remarks>
    public static FileBuilder Dockerfile()
    {
      return new FileBuilder();
    }

    public static IEngineScope EngineScope(EngineScopeType scope, DockerUri host = null, ICertificatePaths certificates = null)
    {
      return new EngineScope(host, scope, certificates);
    }

    public static NetworkBuilder UseNetwork(string name = null)
    {
      return new Builder().UseNetwork(name);
    }

    public static VolumeBuilder UseVolume(string name = null)
    {
      return new Builder().UseVolume(name);
    }
    #endregion

    #region Host Support

    public static Hosts Hosts()
    {
      return new Hosts();
    }

    public static IList<IHostService> Discover(bool preferNative = false)
    {
      return Hosts().Discover(preferNative);
    }
    public static IHostService Native()
    {
      return Hosts().Native();
    }

    public static IHostService FromMachineName(string name, bool isWindowsHost = false, bool throwIfNotStarted = false)
    {
      return Hosts().FromMachineName(name, isWindowsHost, throwIfNotStarted);
    }
    #endregion
  }
}
