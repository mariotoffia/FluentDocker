using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Volumes;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.Xunit;
using Xunit;

namespace FluentDocker.Tests.Integration.Testing
{
  [Trait("Category", "Integration")]
  public class ContainerResourceIntegrationTests : TestingResourceIntegrationTestBase
  {
    [Fact]
    public async Task ContainerResource_InitializesRunsAndDisposesContainer()
    {
      var ct = TestContext.Current.CancellationToken;
      await EnsureImageAsync(NginxImage, ct);
      var name = UniqueName("fd-tst-ctr");
      var resource = new ContainerResource(
          Kernel,
          b => b.UseImage(NginxImage).WithName(name),
          Options());

      try
      {
        await resource.InitializeAsync(ct);
        var inspect = await ContainerDriver.InspectAsync(Context, resource.Container.Id, ct);
        Assert.True(inspect.Success, inspect.Error);
        Assert.True(inspect.Data.State.Running);
      }
      finally
      {
        await resource.DisposeAsync();
      }

      await AssertNoContainersByNameAsync(name, ct);
    }
  }

  [Trait("Category", "Integration")]
  public class NetworkResourceIntegrationTests : TestingResourceIntegrationTestBase
  {
    [Fact]
    public async Task NetworkResource_InitializesAndDisposesNetwork()
    {
      var ct = TestContext.Current.CancellationToken;
      var name = UniqueName("fd-tst-net");
      var resource = new NetworkResource(Kernel, c => c.Name = name, Options());

      try
      {
        await resource.InitializeAsync(ct);
        var inspect = await resource.InspectAsync(ct);
        Assert.Equal(name, inspect.Name);
      }
      finally
      {
        await resource.DisposeAsync();
      }

      var list = await NetworkDriver.ListAsync(Context, new NetworkListFilter { Name = name }, ct);
      Assert.True(list.Success, list.Error);
      Assert.DoesNotContain(list.Data ?? [], n => n.Name == name);
    }
  }

  [Trait("Category", "Integration")]
  public class VolumeResourceIntegrationTests : TestingResourceIntegrationTestBase
  {
    [Fact]
    public async Task VolumeResource_InitializesAndDisposesVolume()
    {
      var ct = TestContext.Current.CancellationToken;
      var name = UniqueName("fd-tst-vol");
      var resource = new VolumeResource(Kernel, c => c.Name = name, Options());

      try
      {
        await resource.InitializeAsync(ct);
        var inspect = await resource.InspectAsync(ct);
        Assert.Equal(name, inspect.Name);
      }
      finally
      {
        await resource.DisposeAsync();
      }

      var list = await VolumeDriver.ListAsync(Context, new VolumeListFilter { Name = name }, ct);
      Assert.True(list.Success, list.Error);
      Assert.DoesNotContain(list.Data ?? [], v => v.Name == name);
    }
  }

  [Trait("Category", "Integration")]
  public class ImageResourceIntegrationTests : TestingResourceIntegrationTestBase
  {
    [Fact]
    public async Task ImageResource_PullsAndDisposesImage()
    {
      var ct = TestContext.Current.CancellationToken;
      var resource = new ImageResource(Kernel, "hello-world", "latest", true, Options());

      await resource.InitializeAsync(ct);
      Assert.False(string.IsNullOrEmpty(resource.ImageId));
      await resource.DisposeAsync();

      var inspect = await ImageDriver.InspectAsync(Context, HelloWorldImage, ct);
      Assert.False(inspect.Success);
    }
  }

  [Trait("Category", "Integration")]
  public class ComposeResourceIntegrationTests : TestingResourceIntegrationTestBase
  {
    [Fact]
    public async Task ComposeResource_InitializesServiceAndDisposesProject()
    {
      var ct = TestContext.Current.CancellationToken;
      await EnsureImageAsync(BusyboxImage, ct);
      var project = UniqueName("fdtstcmp").Replace("-", string.Empty, StringComparison.Ordinal);
      var composeFile = ResourcePath("Resources/ComposeTests/TestingResource/docker-compose.yml");
      var resource = new ComposeResource(
          Kernel,
          c => c.WithComposeFile(composeFile).WithProjectName(project),
          Options());

      try
      {
        await resource.InitializeAsync(ct);
        var services = await resource.ListServicesAsync(ct);
        Assert.Contains(services, s => s.Name.Contains("app", StringComparison.Ordinal));
      }
      finally
      {
        await resource.DisposeAsync();
      }

      var list = await ContainerDriver.ListAsync(
          Context,
          new ContainerListFilter
          {
            All = true,
            Labels = { ["com.docker.compose.project"] = project }
          },
          ct);
      Assert.True(list.Success, list.Error);
      Assert.False((list.Data ?? []).Any());
    }
  }

  [Trait("Category", "Integration")]
  public class XunitContainerFixtureIntegrationTests : IClassFixture<RealBusyboxFixture>
  {
    private readonly RealBusyboxFixture _fixture;

    public XunitContainerFixtureIntegrationTests(RealBusyboxFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task XunitContainerFixtureBase_InitializesRealContainer()
    {
      Assert.NotNull(_fixture.Container);
      var inspected = await _fixture.Container.InspectAsync(TestContext.Current.CancellationToken);
      Assert.True(inspected.State.Running);
    }
  }

  [Trait("Category", "Integration")]
  public class OrphanCleanupIntegrationTests : TestingResourceIntegrationTestBase
  {
    [Fact]
    public async Task OrphanCleanup_PreservesOldRunningLabeledContainer()
    {
      var ct = TestContext.Current.CancellationToken;
      await EnsureImageAsync(BusyboxImage, ct);
      var name = UniqueName("fd-tst-orphan");
      var labels = SessionLabel.CreateLabels("orphan-" + Guid.NewGuid().ToString("N"));
      labels[SessionLabel.CreatedAtKey] = DateTime.UtcNow.AddHours(-2).ToString("o");
      var run = await ContainerDriver.RunAsync(
          Context,
          new ContainerCreateConfig
          {
            Image = BusyboxImage,
            Name = name,
            Command = ["sh", "-c", "while true; do sleep 1; done"],
            Labels = labels
          },
          ct);
      Assert.True(run.Success, run.Error);

      try
      {
        var result = await OrphanCleanup.CleanupOrphanedResourcesAsync(
            Kernel, DriverId, SessionId, ct);

        Assert.Equal(0, result.ContainersRemoved);
        var inspect = await ContainerDriver.InspectAsync(Context, run.Data.Id, ct);
        Assert.True(inspect.Success, inspect.Error);
        Assert.True(inspect.Data.State.Running);
      }
      finally
      {
        await ContainerDriver.RemoveAsync(Context, name, true, true, ct);
      }
    }
  }

  public sealed class RealBusyboxFixture : XunitContainerFixtureBase
  {
    protected override Func<Task<FluentDocker.Kernel.FluentDockerKernel>>? KernelFactory =>
        async () =>
        {
          var kernel = await TestingResourceIntegrationTestBase.CreateKernelAsync()
              .ConfigureAwait(false);
          var driver = kernel.SysCtl<IImageDriver>("docker");
          await driver.PullAsync(
              new FluentDocker.Model.Drivers.DriverContext("docker"),
              "nginx",
              "alpine",
              cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
          return kernel;
        };

    protected override DockerResourceOptions? GetOptions() => new()
    {
      EnableSessionLabels = true,
      InitializationTimeout = TimeSpan.FromSeconds(60),
      TeardownTimeout = TimeSpan.FromSeconds(30)
    };

    protected override void ConfigureContainer(IContainerBuilder builder)
    {
      builder
          .UseImage(TestingResourceIntegrationTestBase.NginxImage)
          .WithName(TestingResourceIntegrationTestBase.UniqueName("fd-tst-xunit"));
    }
  }
}
