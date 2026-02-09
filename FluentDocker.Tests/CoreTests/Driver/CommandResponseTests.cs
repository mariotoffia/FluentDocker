using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Tests for CommandResponse class.
  /// </summary>
  [Trait("Category", "Unit")]
  public class CommandResponseTests
  {
    [Fact]
    public void Ok_CreatesSuccessResponse()
    {
      // Act
      var response = CommandResponse<string>.Ok("test data");

      // Assert
      Assert.True(response.Success);
      Assert.Equal("test data", response.Data);
      Assert.Equal(0, response.ExitCode);
      Assert.Null(response.Error);
    }

    [Fact]
    public void Ok_WithOutput_IncludesOutput()
    {
      // Act
      var response = CommandResponse<string>.Ok("test data", "output text");

      // Assert
      Assert.True(response.Success);
      Assert.Equal("test data", response.Data);
      Assert.Equal("output text", response.Output);
    }

    [Fact]
    public void Fail_CreatesFailureResponse()
    {
      // Act
      var response = CommandResponse<string>.Fail("error message");

      // Assert
      Assert.False(response.Success);
      Assert.Equal("error message", response.Error);
      Assert.Equal(ErrorCodes.General.Unknown, response.ErrorCode);
      Assert.Equal(-1, response.ExitCode);
    }

    [Fact]
    public void Fail_WithErrorCode_IncludesCode()
    {
      // Act
      var response = CommandResponse<string>.Fail("error", ErrorCodes.Container.NotFound, 1);

      // Assert
      Assert.False(response.Success);
      Assert.Equal("error", response.Error);
      Assert.Equal(ErrorCodes.Container.NotFound, response.ErrorCode);
      Assert.Equal(1, response.ExitCode);
    }

    [Fact]
    public void Fail_WithContext_IncludesContext()
    {
      // Arrange
      var context = new ErrorContext("TestOperation")
      {
        DriverId = "docker",
        Host = "localhost",
        ExitCode = 127
      };

      // Act
      var response = CommandResponse<string>.Fail("error", ErrorCodes.Driver.NotAvailable, context, 127);

      // Assert
      Assert.False(response.Success);
      Assert.Equal(context, response.ErrorContext);
      Assert.Equal("docker", response.ErrorContext.DriverId);
    }

    [Fact]
    public void Success_DefaultExitCodeIsZero()
    {
      // Act
      var response = CommandResponse<int>.Ok(42);

      // Assert
      Assert.Equal(0, response.ExitCode);
    }

    [Fact]
    public void Failure_DefaultExitCodeIsMinusOne()
    {
      // Act
      var response = CommandResponse<int>.Fail("error");

      // Assert
      Assert.Equal(-1, response.ExitCode);
    }
  }
}

