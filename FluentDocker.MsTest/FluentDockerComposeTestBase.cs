using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Kernel;
using FluentDocker.Model.Common;
using FluentDocker.Model.Kernel;
using FluentDocker.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDocker.MsTest
{
  /// <summary>
  /// Base class for Docker Compose integration tests.
  /// </summary>
  [Experimental]
  [Obsolete("Use FluentDocker.Testing.Core.ComposeResource with " +
            "FluentDocker.Testing.MsTest.MsTestResourceHelpers instead. " +
            "See docs/testing/migration-from-legacy.md for migration guide.")]
  public abstract class FluentDockerComposeTestBase
  {
    protected IComposeService Service { get; private set; }
    protected FluentDockerKernel Kernel { get; private set; }
    protected string DriverId { get; private set; } = "docker-cli";
    protected readonly string ComposeFile;

    protected FluentDockerComposeTestBase(TemplateString fqPathDockerComposeFile) => ComposeFile = fqPathDockerComposeFile;

    /// <summary>
    /// Override to customize the kernel setup.
    /// </summary>
    protected virtual async Task<FluentDockerKernel> CreateKernelAsync()
    {
      return await FluentDockerKernel.Create()
          .WithDockerCli(DriverId, d => d.AsDefault())
          .BuildAsync();
    }

    /// <summary>
    /// Override to configure compose options.
    /// </summary>
    protected virtual void ConfigureCompose(IComposeBuilder builder)
    {
      builder
          .WithComposeFile(ComposeFile)
          .WithRemoveOrphans();
    }

    [TestInitialize]
    public void Initialize()
    {
      InitializeAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Async initialization.
    /// </summary>
    protected async Task InitializeAsync()
    {
      Kernel = await CreateKernelAsync();

      var builder = new Builder();
      builder.WithinDriver(DriverId, Kernel);
      builder.UseCompose(ConfigureCompose);

      // BuildAsync already starts services. Do NOT call StartAsync again
      // to avoid duplicate-start behavior.
      var results = await builder.BuildAsync();
      if (results.All.Count > 0 && results.All[0] is IComposeService compose)
      {
        Service = compose;
      }

      await OnServiceInitializedAsync();
    }

    [TestCleanup]
    public void TeardownContainer()
    {
      TeardownContainerAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Async teardown.
    /// </summary>
    protected async Task TeardownContainerAsync()
    {
      await OnServiceTearDownAsync();

      var s = Service;
      Service = null;
      if (s != null)
      {
        try
        {
          await s.StopAsync();
          await s.RemoveAsync(force: true);
        }
        catch
        {
          // Ignore cleanup errors
        }
      }

      Kernel?.Dispose();
      Kernel = null;
    }

    /// <summary>
    /// Invoked just before the service is torn down.
    /// </summary>
    protected virtual Task OnServiceTearDownAsync()
    {
      return Task.CompletedTask;
    }

    /// <summary>
    /// Invoked after the service has been created and started.
    /// </summary>
    protected virtual Task OnServiceInitializedAsync()
    {
      return Task.CompletedTask;
    }
  }
}
