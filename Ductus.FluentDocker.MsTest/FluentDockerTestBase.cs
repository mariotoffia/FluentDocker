using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.MsTest
{
  public abstract class FluentDockerTestBase
  {
    protected DockerContainer Container;

    /// <summary>
    /// Builds a <see cref="DockerBuilder"/> and returns it. It will be used to
    /// instantiate a container.
    /// </summary>
    /// <returns>A new instance of populated <see cref="DockerBuilder"/>.</returns>
    protected abstract DockerBuilder Build();

    [TestInitialize]
    public void Initialize()
    {
      Container = Build().Build();
      try
      {
        Container.Create().Start();
      }
      catch (Exception)
      {
        Container.Dispose();
        throw;
      }

      OnContainerInitialized();
    }

    [TestCleanup]
    public void TeardownPostgres()
    {
      OnContainerTearDown();

      var c = Container;
      Container = null;
      try
      {
        c?.Dispose();
      }
      catch (Exception)
      {
        // Ignore
      }
    }

    /// <summary>
    /// Invoked just before the container is teared down.
    /// </summary>
    protected virtual void OnContainerTearDown()
    {
    }

    /// <summary>
    /// Invoked after a container has been created and started.
    /// </summary>
    protected virtual void OnContainerInitialized()
    {
    }
  }
}