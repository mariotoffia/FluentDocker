using FluentDocker.Common;
using FluentDocker.Services.Extensions;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Extensions
{
  /// <summary>
  /// Tests for EnvironmentExtensions.
  /// </summary>
  [Trait("Category", "Unit")]
  public class EnvironmentExtensionTests
  {
    [Fact]
    public void IsNative_ReturnsBoolean()
    {
      // Act
      var result = EnvironmentExtensions.IsNative();

      // Assert - just verify it returns a boolean without throwing
      Assert.IsType<bool>(result);
    }

    [Fact]
    public void IsEmulatedNative_ReturnsBoolean()
    {
      // Act
      var result = EnvironmentExtensions.IsEmulatedNative();

      // Assert
      Assert.IsType<bool>(result);
    }

    [Fact]
    public void IsNative_And_IsEmulatedNative_AreMutuallyExclusive()
    {
      // Act
      var isNative = EnvironmentExtensions.IsNative();
      var isEmulated = EnvironmentExtensions.IsEmulatedNative();

      // Assert - they should be opposites
      Assert.NotEqual(isNative, isEmulated);
    }

    [Fact]
    public void IsDockerDnsAvailable_ReturnsBoolean()
    {
      // Act
      var result = EnvironmentExtensions.IsDockerDnsAvailable();

      // Assert
      Assert.IsType<bool>(result);
    }

    [Fact]
    public void GetDockerHostAddress_ReturnsIPAddress()
    {
      // Act
      var result = EnvironmentExtensions.GetDockerHostAddress();

      // Assert
      Assert.NotNull(result);
    }

    [Fact]
    public void GetDockerHostAddress_WithCache_ReturnsSameAddress()
    {
      // Act
      var result1 = EnvironmentExtensions.GetDockerHostAddress(useCache: true);
      var result2 = EnvironmentExtensions.GetDockerHostAddress(useCache: true);

      // Assert - cached values should be equal
      Assert.Equal(result1, result2);
    }

    [Fact]
    public void GetLocalhostAddress_ReturnsValidAddress()
    {
      // Act
      var result = EnvironmentExtensions.GetLocalhostAddress();

      // Assert
      Assert.NotNull(result);
      Assert.NotEmpty(result);
      // Should be either "localhost" or "127.0.0.1"
      Assert.True(result == "localhost" || result == "127.0.0.1");
    }

    [Fact]
    public void IsRunningInDocker_ReturnsBoolean()
    {
      // Act
      var result = EnvironmentExtensions.IsRunningInDocker();

      // Assert
      Assert.IsType<bool>(result);
    }

    [Fact]
    public void GetDockerSocketPath_ReturnsPath()
    {
      // Act
      var result = EnvironmentExtensions.GetDockerSocketPath();

      // Assert
      Assert.NotNull(result);
      Assert.NotEmpty(result);
    }

    [Fact]
    public void GetDockerSocketPath_ReturnsValidPathForPlatform()
    {
      // Act
      var result = EnvironmentExtensions.GetDockerSocketPath();

      // Assert - should contain expected patterns
      if (FdOs.IsWindows())
      {
        Assert.Contains("pipe", result);
      }
      else
      {
        Assert.Contains("docker.sock", result);
      }
    }

    [Fact]
    public void IsRootless_ReturnsBoolean()
    {
      // Act
      var result = EnvironmentExtensions.IsRootless();

      // Assert
      Assert.IsType<bool>(result);
    }
  }
}

