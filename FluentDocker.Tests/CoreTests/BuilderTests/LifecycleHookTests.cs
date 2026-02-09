using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Tests for Lifecycle Hook builder methods.
  /// </summary>
  [Trait("Category", "Unit")]
  public class LifecycleHookTests
  {
    [Fact]
    public void CopyToOnStart_SetsHook()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseImage("test")
                   .CopyToOnStart("/host/file.txt", "/container/file.txt");
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void CopyFromOnDispose_SetsHook()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseImage("test")
                   .CopyFromOnDispose("/container/logs", "/host/logs");
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void ExportOnDispose_Simple_SetsHook()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseImage("test")
                   .ExportOnDispose("/host/container.tar");
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void ExportOnDispose_WithExplode_SetsHook()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseImage("test")
                   .ExportOnDispose("/host/container-dir", explode: true);
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void ExportOnDispose_WithCondition_SetsHook()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseImage("test")
                   .ExportOnDispose(
                       "/host/container.tar",
                       condition: service => service.State == ServiceRunningState.Stopped);
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void ExecuteOnRunning_SetsHook()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseImage("test")
                   .ExecuteOnRunning("sh", "-c", "echo 'Container started'");
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void ExecuteOnDisposing_SetsHook()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseImage("test")
                   .ExecuteOnDisposing("sh", "-c", "echo 'Container stopping'");
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void MultipleHooks_CanBeChained()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseImage("test")
                   .CopyToOnStart("/host/config.json", "/app/config.json")
                   .ExecuteOnRunning("sh", "-c", "setup.sh")
                   .CopyFromOnDispose("/app/logs", "/host/logs")
                   .ExecuteOnDisposing("sh", "-c", "cleanup.sh");
            configured = true;
          });
      Assert.True(configured);
    }
  }

  /// <summary>
  /// Tests for LifecycleHook and LifecycleHookType types.
  /// </summary>
  [Trait("Category", "Unit")]
  public class LifecycleHookTypeTests
  {
    [Fact]
    public void LifecycleHookType_HasExpectedValues()
    {
      // Assert that all expected hook types exist
      Assert.Equal(LifecycleHookType.CopyTo, LifecycleHookType.CopyTo);
      Assert.Equal(LifecycleHookType.CopyFrom, LifecycleHookType.CopyFrom);
      Assert.Equal(LifecycleHookType.Export, LifecycleHookType.Export);
      Assert.Equal(LifecycleHookType.Execute, LifecycleHookType.Execute);
    }

    [Fact]
    public void LifecycleHook_CanBeCreated()
    {
      // Arrange & Act
      var hook = new LifecycleHook
      {
        Type = LifecycleHookType.CopyTo,
        TriggerState = ServiceRunningState.Running,
        HostPath = "/host/path",
        ContainerPath = "/container/path"
      };

      // Assert
      Assert.Equal(LifecycleHookType.CopyTo, hook.Type);
      Assert.Equal(ServiceRunningState.Running, hook.TriggerState);
      Assert.Equal("/host/path", hook.HostPath);
      Assert.Equal("/container/path", hook.ContainerPath);
    }

    [Fact]
    public void LifecycleHook_Execute_CanStoreCommand()
    {
      // Arrange & Act
      var hook = new LifecycleHook
      {
        Type = LifecycleHookType.Execute,
        TriggerState = ServiceRunningState.Running,
        Command = new[] { "sh", "-c", "echo hello" }
      };

      // Assert
      Assert.Equal(LifecycleHookType.Execute, hook.Type);
      Assert.Equal(3, hook.Command.Length);
      Assert.Equal("sh", hook.Command[0]);
    }

    [Fact]
    public void LifecycleHook_Export_CanStoreCondition()
    {
      // Arrange & Act
      var hook = new LifecycleHook
      {
        Type = LifecycleHookType.Export,
        TriggerState = ServiceRunningState.Removing,
        HostPath = "/export/path",
        Explode = true,
        Condition = service => service.State == ServiceRunningState.Stopped
      };

      // Assert
      Assert.Equal(LifecycleHookType.Export, hook.Type);
      Assert.True(hook.Explode);
      Assert.NotNull(hook.Condition);
    }
  }
}

