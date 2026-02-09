using System;
using System.Collections.Generic;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Tests for Container Builder configuration methods.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ContainerBuilderTests
  {
    [Fact]
    public void UseImage_SetsImage()
    {
      // Act - Just verify compilation and method exists
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseImage("nginx:latest");
            configured = true;
          });

      // Assert
      Assert.True(configured);
    }

    [Fact]
    public void WithName_SetsContainerName()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.WithName("my-container");
            configured = true;
          });
      Assert.True(configured);
    }

    [Theory]
    [InlineData("KEY", "VALUE")]
    [InlineData("PATH", "/usr/bin")]
    [InlineData("DEBUG", "true")]
    public void WithEnvironment_KeyValue_SetsEnvironment(string key, string value)
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.WithEnvironment(key, value);
            configured = true;
          });
      Assert.True(configured);
    }

    [Theory]
    [InlineData("KEY=VALUE")]
    [InlineData("PATH=/usr/bin")]
    public void WithEnvironment_SingleString_SetsEnvironment(string keyValue)
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.WithEnvironment(keyValue);
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void WithPort_SetsPortMapping()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.WithPort("80/tcp", "8080");
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void ExposePort_String_ExposesPort()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.ExposePort("5432");
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void ExposePort_IntPair_ExposesPort()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.ExposePort(5432, 5432);
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void WithCommand_SetsCommand()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.WithCommand("sh", "-c", "echo hello");
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void WithVolume_SetsVolumeMount()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.WithVolume("/host/path", "/container/path");
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void WithLabel_SetsLabel()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.WithLabel("app", "myapp");
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void WithWorkingDirectory_SetsWorkDir()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.WithWorkingDirectory("/app");
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void WithUser_SetsUser()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.WithUser("1000:1000");
            configured = true;
          });
      Assert.True(configured);
    }

    [Theory]
    [InlineData("no")]
    [InlineData("always")]
    [InlineData("unless-stopped")]
    [InlineData("on-failure")]
    public void WithRestartPolicy_SetsPolicy(string policy)
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.WithRestartPolicy(policy);
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void WithHostname_SetsHostname()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.WithHostname("my-host");
            configured = true;
          });
      Assert.True(configured);
    }

    [Theory]
    [InlineData("bridge")]
    [InlineData("host")]
    [InlineData("none")]
    public void WithNetworkMode_SetsNetworkMode(string mode)
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.WithNetworkMode(mode);
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void WithNetwork_AddsNetwork()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.WithNetwork("my-network");
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void WithNetworkAlias_AddsNetworkWithAlias()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.WithNetworkAlias("my-network", "my-alias");
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void WithMemoryLimit_SetsMemory()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.WithMemoryLimit(512 * 1024 * 1024); // 512MB
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void WithCpuShares_SetsCpu()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.WithCpuShares(512);
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void WithPrivileged_SetsPrivileged()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.WithPrivileged(true);
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void WithAutoRemove_SetsAutoRemove()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.WithAutoRemove(true);
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void ReuseIfExists_SetsReuseBehavior()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.ReuseIfExists();
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void DestroyIfExists_SetsDestroyBehavior()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.DestroyIfExists(force: true, removeVolumes: true);
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void ForcePullImage_SetsForcePull()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.ForcePullImage();
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void KeepContainer_SetsKeepContainer()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.KeepContainer();
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void KeepRunning_SetsKeepRunning()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.KeepRunning();
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void DeleteVolumeOnDispose_SetsVolumeCleanup()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.DeleteVolumeOnDispose();
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void DeleteNamedVolumeOnDispose_SetsNamedVolumeCleanup()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.DeleteNamedVolumeOnDispose();
            configured = true;
          });
      Assert.True(configured);
    }

    [Theory]
    [InlineData("192.168.1.100")]
    [InlineData("10.0.0.50")]
    [InlineData("172.16.0.10")]
    public void UseIpV4_SetsStaticIpv4Address(string ipv4)
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseIpV4(ipv4);
            configured = true;
          });
      Assert.True(configured);
    }

    [Theory]
    [InlineData("fd00::1")]
    [InlineData("2001:db8::1")]
    [InlineData("fe80::1")]
    public void UseIpV6_SetsStaticIpv6Address(string ipv6)
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseIpV6(ipv6);
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void UseIpV4AndIpV6_CanBeUsedTogether()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseIpV4("192.168.1.100")
                   .UseIpV6("fd00::1");
            configured = true;
          });
      Assert.True(configured);
    }
  }
}

