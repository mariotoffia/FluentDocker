using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.MsTest
{
  [Experimental]
  public abstract class FluentDockerComposeTestBase
  {
    protected ICompositeService Service;
    protected readonly string ComposeFile;
    
    protected FluentDockerComposeTestBase(TemplateString fqPathDockerComposeFile)
    {
      ComposeFile = fqPathDockerComposeFile;
    }
    // https://github.com/libgit2/libgit2sharp/tree/master/LibGit2Sharp.Tests
    [TestInitialize]
    public void Initialize()
    {
      Service = Build().Build();
      try
      {
        Service.Start();
      }
      catch
      {
        Service.Dispose();
        throw;
      }

      OnServiceInitialized();
    }

    [TestCleanup]
    public void TeardownContainer()
    {
      OnServiceTearDown();

      var c = Service;
      Service = null;
      try
      {
        c?.Dispose();
      }
      catch
      {
        // Ignore
      }
    }

    protected virtual CompositeBuilder Build()
    {
      return new Builder()
        .UseContainer()
        .UseCompose()
        .FromFile(ComposeFile)
        .RemoveOrphans();
    }
    
    /// <summary>
    ///   Invoked just before the service is teared down.
    /// </summary>
    protected virtual void OnServiceTearDown()
    {
    }

    /// <summary>
    ///   Invoked after a container has been created and started.
    /// </summary>
    protected virtual void OnServiceInitialized()
    {
    }    
  }
}