using System;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.XUnit
{
  public abstract class FluentDockerTestBase : IDisposable
  {
    protected IContainerService Container;

    protected abstract ContainerBuilder Build();

    public FluentDockerTestBase()
    {
      Container = Build().Build();
      try
      {
        Container.Start();
      }
      catch
      {
        Container.Dispose();
        throw;
      }

      OnContainerInitialized();
    }

    public void Dispose()
    {
      OnContainerTearDown();

      var c = Container;
      Container = null;
      try
      {
        c?.Dispose();
      }
      catch
      {
        // Ignore
      }
    }

    /// <summary>
    ///   Invoked just before the container is teared down.
    /// </summary>
    protected virtual void OnContainerTearDown()
    {
    }

    /// <summary>
    ///   Invoked after a container has been created and started.
    /// </summary>
    protected virtual void OnContainerInitialized()
    {
    }
  }
}
