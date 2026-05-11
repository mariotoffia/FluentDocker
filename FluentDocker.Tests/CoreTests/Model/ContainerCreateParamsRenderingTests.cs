using System;
using System.Collections.Generic;
using System.Net;
using FluentDocker.Model.Containers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  [Trait("Category", "Unit")]
  public class ContainerCreateParamsRenderingTests
  {
    [Fact]
    public void ToString_DefaultParams_ReturnsEmpty()
    {
      // Arrange
      var p = new ContainerCreateParams();

      // Act
      var result = p.ToString();

      // Assert - all defaults are unset so the output should be empty
      Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ToString_WithName_RendersNameFlag()
    {
      // Arrange
      var p = new ContainerCreateParams { Name = "test" };

      // Act
      var result = p.ToString();

      // Assert
      Assert.Contains("--name test", result);
    }

    [Fact]
    public void ToString_WithPortMappings_RendersPortFlags()
    {
      // Arrange
      var p = new ContainerCreateParams
      {
        PortMappings = ["8080:80", "9090:90"]
      };

      // Act
      var result = p.ToString();

      // Assert
      Assert.Contains("-p 8080:80", result);
      Assert.Contains("-p 9090:90", result);
    }

    [Fact]
    public void ToString_WithPublishAllPorts_RendersDashP()
    {
      // Arrange
      var p = new ContainerCreateParams
      {
        PublishAllPorts = true,
        PortMappings = ["8080:80"]
      };

      // Act
      var result = p.ToString();

      // Assert - PublishAllPorts=true renders -P and ignores individual mappings
      Assert.Contains(" -P", result);
      Assert.DoesNotContain("-p ", result);
    }

    [Fact]
    public void ToString_WithEnvironment_RendersEnvFlags()
    {
      // Arrange
      var p = new ContainerCreateParams
      {
        Environment = ["FOO=bar", "BAZ=qux"]
      };

      // Act
      var result = p.ToString();

      // Assert
      Assert.Contains("-e FOO=bar", result);
      Assert.Contains("-e BAZ=qux", result);
    }

    [Fact]
    public void ToString_WithVolumes_RendersVolumeFlags()
    {
      // Arrange
      var p = new ContainerCreateParams
      {
        Volumes = ["/host:/container", "/data:/data:ro"]
      };

      // Act
      var result = p.ToString();

      // Assert
      Assert.Contains("-v /host:/container", result);
      Assert.Contains("-v /data:/data:ro", result);
    }

    [Fact]
    public void ToString_WithNetwork_RendersNetworkFlag()
    {
      // Arrange
      var p = new ContainerCreateParams { Network = "mynet" };

      // Act
      var result = p.ToString();

      // Assert
      Assert.Contains("--network mynet", result);
    }

    [Fact]
    public void ToString_WithNetworkAndAlias_RendersAlias()
    {
      // Arrange
      var p = new ContainerCreateParams
      {
        Network = "mynet",
        Alias = "myalias"
      };

      // Act
      var result = p.ToString();

      // Assert
      Assert.Contains("--network mynet", result);
      Assert.Contains("--network-alias myalias", result);
    }

    [Fact]
    public void ToString_WithoutNetwork_OmitsAlias()
    {
      // Arrange - Alias is set but Network is not
      var p = new ContainerCreateParams
      {
        Alias = "myalias"
      };

      // Act
      var result = p.ToString();

      // Assert - alias should be omitted when network is not set
      Assert.DoesNotContain("--network-alias", result);
    }

    [Fact]
    public void ToString_WithRestartAlways_RendersRestartPolicy()
    {
      // Arrange
      var p = new ContainerCreateParams
      {
        RestartPolicy = RestartPolicy.Always
      };

      // Act
      var result = p.ToString();

      // Assert
      Assert.Contains("--restart always", result);
    }

    [Fact]
    public void ToString_WithRestartOnFailure_RendersRestartPolicy()
    {
      // Arrange
      var p = new ContainerCreateParams
      {
        RestartPolicy = RestartPolicy.OnFailure
      };

      // Act
      var result = p.ToString();

      // Assert
      Assert.Contains("--restart on-failure", result);
    }

    [Fact]
    public void ToString_WithRestartUnlessStopped_RendersRestartPolicy()
    {
      // Arrange
      var p = new ContainerCreateParams
      {
        RestartPolicy = RestartPolicy.UnlessStopped
      };

      // Act
      var result = p.ToString();

      // Assert
      Assert.Contains("--restart unless-stopped", result);
    }

    [Fact]
    public void ToString_WithMemory_RendersMemoryFlag()
    {
      // Arrange
      var p = new ContainerCreateParams { Memory = "512m" };

      // Act
      var result = p.ToString();

      // Assert
      Assert.Contains("--memory=512m", result);
    }

    [Fact]
    public void ToString_WithPrivileged_RendersPrivilegedFlag()
    {
      // Arrange
      var p = new ContainerCreateParams { Privileged = true };

      // Act
      var result = p.ToString();

      // Assert
      Assert.Contains("--privileged", result);
    }

    [Fact]
    public void ToString_WithAutoRemove_RendersRmFlag()
    {
      // Arrange
      var p = new ContainerCreateParams { AutoRemoveContainer = true };

      // Act
      var result = p.ToString();

      // Assert
      Assert.Contains("--rm", result);
    }

    [Fact]
    public void ToString_WithHealthCheck_RendersHealthCheckFlags()
    {
      // Arrange
      var p = new ContainerCreateParams
      {
        HealthCheckCmd = "curl -f http://localhost/",
        HealthCheckRetries = 5
      };

      // Act
      var result = p.ToString();

      // Assert
      Assert.Contains("--health-cmd=curl -f http://localhost/", result);
      Assert.Contains("--health-retries=5", result);
    }

    [Fact]
    public void ToString_WithLabels_RendersLabelFlags()
    {
      // Arrange
      var p = new ContainerCreateParams
      {
        Labels = ["env=prod", "team=backend"]
      };

      // Act
      var result = p.ToString();

      // Assert
      Assert.Contains("-l env=prod", result);
      Assert.Contains("-l team=backend", result);
    }

    [Fact]
    public void ToString_WithHostIpMappings_RendersAddHostFlags()
    {
      // Arrange
      var p = new ContainerCreateParams
      {
        HostIpMappings =
        [
          Tuple.Create("myhost", IPAddress.Parse("192.168.1.100")),
          Tuple.Create("dbhost", IPAddress.Parse("10.0.0.5"))
        ]
      };

      // Act
      var result = p.ToString();

      // Assert
      Assert.Contains("--add-host=myhost:192.168.1.100", result);
      Assert.Contains("--add-host=dbhost:10.0.0.5", result);
    }

    [Fact]
    public void ToString_WithHostname_RendersHostnameFlag()
    {
      // Arrange
      var p = new ContainerCreateParams { Hostname = "myhost" };

      // Act
      var result = p.ToString();

      // Assert
      Assert.Contains("--hostname myhost", result);
    }

    [Fact]
    public void ToString_WithInteractiveAndTty_RendersFlags()
    {
      // Arrange
      var p = new ContainerCreateParams
      {
        Interactive = true,
        Tty = true
      };

      // Act
      var result = p.ToString();

      // Assert
      Assert.Contains(" -i", result);
      Assert.Contains(" -t", result);
    }
  }
}
