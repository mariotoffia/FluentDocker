using System;
using System.Collections.Generic;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  /// <summary>
  /// Unit tests for ErrorContextExtensions.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ErrorContextExtensionsTests
  {
    [Fact]
    public void WithMetadata_AddsMetadata()
    {
      // Arrange
      var context = new ErrorContext("TestOperation");

      // Act
      context.WithMetadata("key1", "value1")
             .WithMetadata("key2", "value2");

      // Assert
      Assert.Equal("value1", context.Metadata["key1"]);
      Assert.Equal("value2", context.Metadata["key2"]);
    }

    [Fact]
    public void WithDriverId_SetsDriverId()
    {
      // Arrange
      var context = new ErrorContext();

      // Act
      context.WithDriverId("docker");

      // Assert
      Assert.Equal("docker", context.DriverId);
    }

    [Fact]
    public void WithHost_SetsHost()
    {
      // Arrange
      var context = new ErrorContext();

      // Act
      context.WithHost("localhost:2375");

      // Assert
      Assert.Equal("localhost:2375", context.Host);
    }

    [Fact]
    public void WithExitCode_SetsExitCode()
    {
      // Arrange
      var context = new ErrorContext();

      // Act
      context.WithExitCode(137);

      // Assert
      Assert.Equal(137, context.ExitCode);
    }

    [Fact]
    public void WithStdOut_SetsStdOut()
    {
      // Arrange
      var context = new ErrorContext();

      // Act
      context.WithStdOut("container output");

      // Assert
      Assert.Equal("container output", context.StdOut);
    }

    [Fact]
    public void WithStdErr_SetsStdErr()
    {
      // Arrange
      var context = new ErrorContext();

      // Act
      context.WithStdErr("error message");

      // Assert
      Assert.Equal("error message", context.StdErr);
    }

    [Fact]
    public void WithOperationId_SetsOperationId()
    {
      // Arrange
      var context = new ErrorContext();
      var opId = Guid.NewGuid().ToString();

      // Act
      context.WithOperationId(opId);

      // Assert
      Assert.Equal(opId, context.OperationId);
    }

    [Fact]
    public void FluentChaining_AllMethodsChainable()
    {
      // Act
      var context = new ErrorContext()
          .WithOperation("CreateContainer")
          .WithDriverId("docker")
          .WithHost("localhost")
          .WithExitCode(1)
          .WithStdOut("stdout")
          .WithStdErr("stderr")
          .WithOperationId("op-123")
          .WithMetadata("containerId", "abc123");

      // Assert
      Assert.Equal("CreateContainer", context.Operation);
      Assert.Equal("docker", context.DriverId);
      Assert.Equal("localhost", context.Host);
      Assert.Equal(1, context.ExitCode);
      Assert.Equal("stdout", context.StdOut);
      Assert.Equal("stderr", context.StdErr);
      Assert.Equal("op-123", context.OperationId);
      Assert.Equal("abc123", context.Metadata["containerId"]);
    }

    [Fact]
    public void ForContainer_CreatesContextWithContainerId()
    {
      // Act
      var context = ErrorContextExtensions.ForContainer("Start", "abc123", "docker");

      // Assert
      Assert.Equal("Start", context.Operation);
      Assert.Equal("abc123", context.Metadata["containerId"]);
      Assert.Equal("docker", context.DriverId);
    }

    [Fact]
    public void ForNetwork_CreatesContextWithNetworkId()
    {
      // Act
      var context = ErrorContextExtensions.ForNetwork("Create", "net123", "docker");

      // Assert
      Assert.Equal("Create", context.Operation);
      Assert.Equal("net123", context.Metadata["networkId"]);
      Assert.Equal("docker", context.DriverId);
    }

    [Fact]
    public void ForVolume_CreatesContextWithVolumeName()
    {
      // Act
      var context = ErrorContextExtensions.ForVolume("Create", "myvolume", "docker");

      // Assert
      Assert.Equal("Create", context.Operation);
      Assert.Equal("myvolume", context.Metadata["volumeName"]);
      Assert.Equal("docker", context.DriverId);
    }

    [Fact]
    public void ForImage_CreatesContextWithImageName()
    {
      // Act
      var context = ErrorContextExtensions.ForImage("Pull", "nginx:latest", "docker");

      // Assert
      Assert.Equal("Pull", context.Operation);
      Assert.Equal("nginx:latest", context.Metadata["imageName"]);
      Assert.Equal("docker", context.DriverId);
    }

    [Fact]
    public void ForCompose_CreatesContextWithProjectName()
    {
      // Act
      var context = ErrorContextExtensions.ForCompose("Up", "myproject", "docker");

      // Assert
      Assert.Equal("Up", context.Operation);
      Assert.Equal("myproject", context.Metadata["projectName"]);
      Assert.Equal("docker", context.DriverId);
    }
  }

  /// <summary>
  /// Unit tests for CommandResponseExtensions.
  /// </summary>
  [Trait("Category", "Unit")]
  public class CommandResponseExtensionsTests
  {
    [Fact]
    public void EnsureSuccess_OnSuccess_ReturnsData()
    {
      // Arrange
      var response = CommandResponse<string>.Ok("test data");

      // Act
      var result = response.EnsureSuccess("test operation");

      // Assert
      Assert.Equal("test data", result);
    }

    [Fact]
    public void EnsureSuccess_OnFailure_ThrowsDriverException()
    {
      // Arrange
      var response = CommandResponse<string>.Fail("error message", "ERR_001");

      // Act & Assert
      var ex = Assert.Throws<DriverException>(() =>
          response.EnsureSuccess("test operation"));
      Assert.Contains("error message", ex.Message);
    }

    [Fact]
    public void EnsureSuccess_Unit_OnSuccess_DoesNotThrow()
    {
      // Arrange
      var response = CommandResponse<Unit>.Ok(Unit.Default);

      // Act & Assert - should not throw
      response.EnsureSuccess("test operation");
    }

    [Fact]
    public void EnsureSuccess_Unit_OnFailure_ThrowsDriverException()
    {
      // Arrange
      var response = CommandResponse<Unit>.Fail("error message", "ERR_001");

      // Act & Assert
      var ex = Assert.Throws<DriverException>(() =>
          response.EnsureSuccess("test operation"));
      Assert.Contains("error message", ex.Message);
    }

    [Fact]
    public void Map_OnSuccess_TransformsData()
    {
      // Arrange
      var response = CommandResponse<int>.Ok(42);

      // Act
      var result = response.Map(x => x.ToString());

      // Assert
      Assert.True(result.Success);
      Assert.Equal("42", result.Data);
    }

    [Fact]
    public void Map_OnFailure_PreservesError()
    {
      // Arrange
      var response = CommandResponse<int>.Fail("error", "ERR_001");

      // Act
      var result = response.Map(x => x.ToString());

      // Assert
      Assert.False(result.Success);
      Assert.Equal("error", result.Error);
      Assert.Equal("ERR_001", result.ErrorCode);
    }

    [Fact]
    public void GetOrDefault_OnSuccess_ReturnsData()
    {
      // Arrange
      var response = CommandResponse<string>.Ok("data");

      // Act
      var result = response.GetOrDefault("default");

      // Assert
      Assert.Equal("data", result);
    }

    [Fact]
    public void GetOrDefault_OnFailure_ReturnsDefault()
    {
      // Arrange
      var response = CommandResponse<string>.Fail("error", "ERR_001");

      // Act
      var result = response.GetOrDefault("default");

      // Assert
      Assert.Equal("default", result);
    }

    [Fact]
    public void OnSuccess_OnSuccess_ExecutesAction()
    {
      // Arrange
      var response = CommandResponse<int>.Ok(42);
      var executed = false;
      var capturedValue = 0;

      // Act
      response.OnSuccess(x => { executed = true; capturedValue = x; });

      // Assert
      Assert.True(executed);
      Assert.Equal(42, capturedValue);
    }

    [Fact]
    public void OnSuccess_OnFailure_DoesNotExecuteAction()
    {
      // Arrange
      var response = CommandResponse<int>.Fail("error", "ERR_001");
      var executed = false;

      // Act
      response.OnSuccess(x => { executed = true; });

      // Assert
      Assert.False(executed);
    }

    [Fact]
    public void OnFailure_OnSuccess_DoesNotExecuteAction()
    {
      // Arrange
      var response = CommandResponse<int>.Ok(42);
      var executed = false;

      // Act
      response.OnFailure((error, context) => { executed = true; });

      // Assert
      Assert.False(executed);
    }

    [Fact]
    public void OnFailure_OnFailure_ExecutesAction()
    {
      // Arrange
      var response = CommandResponse<int>.Fail("error message", "ERR_001");
      var executed = false;
      string capturedError = null;

      // Act
      response.OnFailure((error, context) => { executed = true; capturedError = error; });

      // Assert
      Assert.True(executed);
      Assert.Equal("error message", capturedError);
    }

    [Fact]
    public void EnrichContext_OnFailure_EnrichesContext()
    {
      // Arrange
      var context = new ErrorContext("Test");
      var response = CommandResponse<int>.Fail("error", "ERR_001", context);

      // Act
      response.EnrichContext(ctx => ctx.WithDriverId("docker"));

      // Assert
      Assert.Equal("docker", response.ErrorContext.DriverId);
    }

    [Fact]
    public void EnrichContext_OnSuccess_DoesNothing()
    {
      // Arrange
      var response = CommandResponse<int>.Ok(42);

      // Act & Assert - should not throw
      response.EnrichContext(ctx => ctx.WithDriverId("docker"));
    }
  }
}
