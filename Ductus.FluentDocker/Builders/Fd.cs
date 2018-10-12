using System;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Builders
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
          run.Invoke((T) service);
        }
      }
      catch
      {
        if (null != name) Logger.Log($"Failed to build / run {name}");
        throw;
      }      
    }
    
    public static void Run<T>(Func<Builder, IBuilder> builder, Action<T> run, string name = null) where T : IService
    {
      try
      {
        using (var service = Build(builder).Build())
        {
          service.Start();
          run.Invoke((T) service);
        }
      }
      catch
      {
        if (null != name) Logger.Log($"Failed to run service {name}");
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

    public static IBuilder Build(Func<Builder, IBuilder> builder)
    {
      try
      {
        return builder.Invoke(new Builder());
      }
      catch
      {
        Logger.Log($"Failed to build");
        throw;
      }
    }
  }
}