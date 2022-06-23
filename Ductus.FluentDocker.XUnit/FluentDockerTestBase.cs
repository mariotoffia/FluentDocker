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
      var containerBuilder = Build();

      this.OnBeforeContainerBuild(containerBuilder);

      Container = containerBuilder.Build();

      try
      {
        this.OnBeforeContainerStart();
        Container.Start();
      }
      catch (Exception ex)
      {
        this.OnBeforeDispose(Container, ex);
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
        this.OnBeforeDispose(c, null);
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
    ///   Invoked just before the container is built.
    /// </summary>
    /// <param name="containerBuilder">The <see cref="ContainerBuilder"/> that is about to be built.</param>
    protected virtual void OnBeforeContainerBuild(ContainerBuilder containerBuilder)
    {
    }

    /// <summary>
    ///   Invoked just after the container is built and before starting it.
    /// </summary>
    protected virtual void OnBeforeContainerStart()
    {
    }

    /// <summary>
    ///   Invoked just before the container is <see cref="System.IDisposable"/>ed.
    /// </summary>
    /// <param name="container">The <see cref="IContainerService"/> that is about to be disposed.</param>
    /// <param name="throwable">The <see cref="Exception"/> that caused the container to be disposed (when starting up).</param>
    /// <remarks>
    ///   This method is invoked either when the container fails to start. In such situation the 
    ///   <paramref name="throwable"/> is not null. It is also called when the test is cleaning up and thus the
    ///   <paramref name="throwable"/> is null. The <paramref name="container"> is always set since the teardown
    ///   will clear the <see cref="Container"/> field. Note that the <paramref name="container"/> may still be null!
    /// </remarks>
    protected virtual void OnBeforeDispose(IContainerService container, Exception throwable)
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
