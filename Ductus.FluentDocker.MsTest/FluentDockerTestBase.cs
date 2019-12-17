using System;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.MsTest
{
  public abstract class FluentDockerTestBase
  {
    protected IContainerService Container;

    protected abstract ContainerBuilder Build();

    [TestInitialize]
    public void Initialize()
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

    [TestCleanup]
    public void TeardownContainer()
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
