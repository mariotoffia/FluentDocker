using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using Xunit;

namespace FluentDocker.Tests.Integration
{
  /// <summary>
  /// Integration tests for Builder with WithinDriver() scoping.
  /// </summary>
  [Trait("Category", "Integration")]
  public class BuilderTests
  {
    [Fact]
    public async Task Builder_WithSingleContainer_CreatesBuildResults()
    {
      // Arrange
      var kernel = await FluentDockerKernel.Create()
          .WithDockerCli("docker", d => d.AsDefault())
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Ensure image is available
      var imageDriver = kernel.SysCtl<FluentDocker.Drivers.IImageDriver>("docker");
      await imageDriver.PullAsync(
          new Model.Drivers.DriverContext("docker"),
          "alpine",
          "latest", cancellationToken: TestContext.Current.CancellationToken);

      try
      {
        // Act
        var results = await new Builder()
            .WithinDriver("docker", kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName($"test-builder-{Guid.NewGuid():N}"))
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(results);
        Assert.Single(results.All);
        Assert.Single(results.ForDriver("docker"));

        // Cleanup
        await results.DisposeAllAsync();
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task Builder_WithMultipleScopes_CreatesBuildResultsForEachScope()
    {
      // Arrange
      var kernel = await FluentDockerKernel.Create()
          .WithDockerCli("docker-1", d => d.AsDefault())
          .WithDockerCli("docker-2", d => d.AsDefault())
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Ensure images are available
      var imageDriver1 = kernel.SysCtl<FluentDocker.Drivers.IImageDriver>("docker-1");
      var imageDriver2 = kernel.SysCtl<FluentDocker.Drivers.IImageDriver>("docker-2");
      await imageDriver1.PullAsync(new Model.Drivers.DriverContext("docker-1"), "alpine", "latest", cancellationToken: TestContext.Current.CancellationToken);
      await imageDriver2.PullAsync(new Model.Drivers.DriverContext("docker-2"), "alpine", "latest", cancellationToken: TestContext.Current.CancellationToken);

      try
      {
        // Act
        var results = await new Builder()
            .WithinDriver("docker-1", kernel)
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"d1-c1-{Guid.NewGuid():N}"))
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"d1-c2-{Guid.NewGuid():N}"))
            .WithinDriver("docker-2")  // Reuses kernel
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"d2-c1-{Guid.NewGuid():N}"))
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(results);
        Assert.Equal(3, results.All.Count);
        Assert.Equal(2, results.ForDriver("docker-1").Count);
        Assert.Single(results.ForDriver("docker-2"));

        // Cleanup
        await results.DisposeAllAsync();
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public void Builder_VerifiesCompilation()
    {
      // This test just verifies the Builder API compiles correctly
      var builder = new Builder();
      Assert.NotNull(builder);
    }

    [Fact]
    public async Task Builder_KernelReuse_VerifiesPattern()
    {
      // This test demonstrates the kernel reuse pattern

      var kernel = await FluentDockerKernel.Create()
          .WithDockerCli("docker-1", d => d.AsDefault())
          .WithDockerCli("docker-2", d => d.AsDefault())
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      var builder = new Builder()
          .WithinDriver("docker-1", kernel)  // Sets kernel
                                             // .UseContainer(...) would go here
          .WithinDriver("docker-2");  // Reuses kernel from previous WithinDriver()

      Assert.NotNull(builder);

      kernel.Dispose();
    }
  }
}

