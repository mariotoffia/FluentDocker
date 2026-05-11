using System;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Exceptions
{
  /// <summary>
  /// Tests for custom exception types.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ExceptionTests
  {
    [Fact]
    public void DriverException_WithMessage_CreatesException()
    {
      // Act
      var ex = new DriverException("test error");

      // Assert
      Assert.Equal("test error", ex.Message);
      Assert.Equal(ErrorCodes.General.Unknown, ex.ErrorCode);
      Assert.False(ex.IsTransient);
    }

    [Fact]
    public void DriverException_WithErrorCode_SetsCode()
    {
      // Act
      var ex = new DriverException("test error", ErrorCodes.Driver.NotFound);

      // Assert
      Assert.Equal(ErrorCodes.Driver.NotFound, ex.ErrorCode);
    }

    [Fact]
    public void DriverException_WithContext_SetsContext()
    {
      // Arrange
      var context = new ErrorContext("TestOp");

      // Act
      var ex = new DriverException("test error", ErrorCodes.Driver.NotFound, context);

      // Assert
      Assert.Equal(context, ex.Context);
    }

    [Fact]
    public void DriverException_WithIsTransient_SetsFlag()
    {
      // Arrange
      var context = new ErrorContext();

      // Act
      var ex = new DriverException("test error", ErrorCodes.Driver.NotAvailable, context, isTransient: true);

      // Assert
      Assert.True(ex.IsTransient);
    }

    [Fact]
    public void DriverNotFoundException_HasDriverId()
    {
      // Act
      var ex = new DriverNotFoundException("driver-1");

      // Assert
      Assert.Equal("driver-1", ex.DriverId);
      Assert.Contains("driver-1", ex.Message);
      Assert.Equal(ErrorCodes.Driver.NotFound, ex.ErrorCode);
    }

    [Fact]
    public void DriverNotAvailableException_IsTransient()
    {
      // Act
      var ex = new DriverNotAvailableException("driver-1", "daemon not running");

      // Assert
      Assert.True(ex.IsTransient);
      Assert.Equal("driver-1", ex.DriverId);
      Assert.Equal(ErrorCodes.Driver.NotAvailable, ex.ErrorCode);
    }

    [Fact]
    public void ContainerNotFoundException_HasContainerId()
    {
      // Act
      var ex = new ContainerNotFoundException("container-123");

      // Assert
      Assert.Equal("container-123", ex.ContainerId);
      Assert.Contains("container-123", ex.Message);
      Assert.Equal(ErrorCodes.Container.NotFound, ex.ErrorCode);
    }

    [Fact]
    public void ContainerStartException_IsTransient()
    {
      // Arrange
      var context = new ErrorContext();

      // Act
      var ex = new ContainerStartException("container-123", "port already in use", context);

      // Assert
      Assert.True(ex.IsTransient);
      Assert.Equal(ErrorCodes.Container.StartFailed, ex.ErrorCode);
    }

    [Fact]
    public void ImageNotFoundException_HasImageName()
    {
      // Act
      var ex = new ImageNotFoundException("nginx:latest");

      // Assert
      Assert.Equal("nginx:latest", ex.ImageName);
      Assert.Contains("nginx:latest", ex.Message);
      Assert.Equal(ErrorCodes.Image.NotFound, ex.ErrorCode);
    }

    [Fact]
    public void ImagePullException_IsTransient()
    {
      // Act
      var ex = new ImagePullException("nginx:latest", "network timeout");

      // Assert
      Assert.True(ex.IsTransient);
      Assert.Equal("nginx:latest", ex.ImageName);
      Assert.Equal(ErrorCodes.Image.PullFailed, ex.ErrorCode);
    }

    [Fact]
    public void InterfaceNotSupportedException_HasDriverIdAndInterface()
    {
      // Act
      var ex = new InterfaceNotSupportedException("driver-1", "IContainerDriver");

      // Assert
      Assert.Equal("driver-1", ex.DriverId);
      Assert.Equal("IContainerDriver", ex.InterfaceName);
      Assert.Equal(ErrorCodes.Driver.InterfaceNotSupported, ex.ErrorCode);
    }

    [Fact]
    public void CapabilityNotSupportedException_HasDriverIdAndCapability()
    {
      // Act
      var ex = new CapabilityNotSupportedException("driver-1", "BuildX");

      // Assert
      Assert.Equal("driver-1", ex.DriverId);
      Assert.Equal("BuildX", ex.CapabilityName);
      Assert.Equal(ErrorCodes.Driver.CapabilityNotSupported, ex.ErrorCode);
    }

    [Fact]
    public void DriverException_ToString_IncludesDetails()
    {
      // Arrange
      var context = new ErrorContext("TestOp")
      {
        DriverId = "docker",
        Host = "localhost"
      };
      var ex = new DriverException("test error", ErrorCodes.Container.NotFound, context, isTransient: true);

      // Act
      var str = ex.ToString();

      // Assert
      Assert.Contains("test error", str);
      Assert.Contains(ErrorCodes.Container.NotFound, str);
      Assert.Contains("True", str); // IsTransient
      Assert.Contains("docker", str); // Context
    }
  }
}

