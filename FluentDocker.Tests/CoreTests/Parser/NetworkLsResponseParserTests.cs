using System;
using System.Reflection;
using FluentDocker.Executors;
using FluentDocker.Executors.Parsers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Parser
{
  [Trait("Category", "Unit")]
  public class NetworkLsResponseParserTests
  {
    [Fact]
    public void Process_ValidResponse_ParsesAllFields()
    {
      // Arrange
      var id = Guid.NewGuid().ToString();
      var name = "test-network";
      var driver = "bridge";
      var scope = "local";
      var ipv6 = false;
      var isInternal = true;
      var created = DateTime.Now.ToUniversalTime();

      var stdOut = $"{id};{name};{driver};{scope};{ipv6};{isInternal};{created:yyyy-MM-dd HH:mm:ss.ffffff} +0000 ZZZ";
      var executionResult = CreateProcessExecutionResult("command", stdOut, "", 0);

      var parser = new NetworkLsResponseParser();

      // Act
      var result = parser.Process(executionResult).Response.Data[0];

      // Assert
      Assert.Equal(id, result.Id);
      Assert.Equal(name, result.Name);
      Assert.Equal(driver, result.Driver);
      Assert.Equal(scope, result.Scope);
      Assert.Equal(ipv6, result.IPv6);
      Assert.Equal(isInternal, result.Internal);
      Assert.Equal(created.Date, result.Created.ToUniversalTime().Date);
    }

    [Fact]
    public void Process_NegativeTimezone_ParsesCorrectly()
    {
      // Arrange
      var tzShift = -3;
      var id = Guid.NewGuid().ToString();
      var name = "test-network";
      var driver = "bridge";
      var scope = "local";
      var ipv6 = false;
      var isInternal = true;
      var created = DateTime.Now;

      var stdOut = $"{id};{name};{driver};{scope};{ipv6};{isInternal};{created:yyyy-MM-dd HH:mm:ss.ffffff} {tzShift:00}00 ZZZ";
      var executionResult = CreateProcessExecutionResult("command", stdOut, "", 0);

      var parser = new NetworkLsResponseParser();

      // Act
      var result = parser.Process(executionResult).Response.Data[0];

      // Assert
      Assert.Equal(id, result.Id);
      Assert.Equal(name, result.Name);
      Assert.Equal(driver, result.Driver);
      Assert.Equal(scope, result.Scope);
      Assert.Equal(ipv6, result.IPv6);
      Assert.Equal(isInternal, result.Internal);
    }

    [Fact]
    public void Process_PositiveTimezone_ParsesCorrectly()
    {
      // Arrange
      var tzShift = 5;
      var id = Guid.NewGuid().ToString();
      var name = "my-network";
      var driver = "overlay";
      var scope = "swarm";
      var ipv6 = true;
      var isInternal = false;
      var created = DateTime.Now;

      var stdOut = $"{id};{name};{driver};{scope};{ipv6};{isInternal};{created:yyyy-MM-dd HH:mm:ss.ffffff} +0{tzShift}00 ZZZ";
      var executionResult = CreateProcessExecutionResult("command", stdOut, "", 0);

      var parser = new NetworkLsResponseParser();

      // Act
      var result = parser.Process(executionResult).Response.Data[0];

      // Assert
      Assert.Equal(id, result.Id);
      Assert.Equal(name, result.Name);
      Assert.Equal(driver, result.Driver);
      Assert.Equal(scope, result.Scope);
      Assert.True(result.IPv6);
      Assert.False(result.Internal);
    }

    [Fact]
    public void Process_MultipleNetworks_ParsesAll()
    {
      // Arrange
      var network1 = $"{Guid.NewGuid()};network1;bridge;local;false;false;2024-01-01 12:00:00.000000 +0000 UTC";
      var network2 = $"{Guid.NewGuid()};network2;overlay;swarm;true;true;2024-01-02 13:00:00.000000 +0000 UTC";
      var stdOut = $"{network1}\n{network2}";
      var executionResult = CreateProcessExecutionResult("command", stdOut, "", 0);

      var parser = new NetworkLsResponseParser();

      // Act
      var result = parser.Process(executionResult).Response;

      // Assert
      Assert.True(result.Success);
      Assert.Equal(2, result.Data.Count);
      Assert.Equal("network1", result.Data[0].Name);
      Assert.Equal("network2", result.Data[1].Name);
    }

    [Fact]
    public void Process_NonZeroExitCode_ReturnsError()
    {
      // Arrange
      var executionResult = CreateProcessExecutionResult("command", "", "Error occurred", 1);

      var parser = new NetworkLsResponseParser();

      // Act
      var result = parser.Process(executionResult).Response;

      // Assert
      Assert.False(result.Success);
      Assert.Empty(result.Data);
    }

    [Fact]
    public void Process_EmptyOutput_ReturnsNoResponse()
    {
      // Arrange
      var executionResult = CreateProcessExecutionResult("command", "", "", 0);

      var parser = new NetworkLsResponseParser();

      // Act
      var result = parser.Process(executionResult).Response;

      // Assert
      Assert.False(result.Success);
      Assert.Empty(result.Data);
    }

    [Fact]
    public void Process_MinimalFields_ParsesBasicData()
    {
      // Arrange - only 4 required fields
      var id = Guid.NewGuid().ToString();
      var stdOut = $"{id};network1;bridge;local";
      var executionResult = CreateProcessExecutionResult("command", stdOut, "", 0);

      var parser = new NetworkLsResponseParser();

      // Act
      var result = parser.Process(executionResult).Response.Data[0];

      // Assert
      Assert.Equal(id, result.Id);
      Assert.Equal("network1", result.Name);
      Assert.Equal("bridge", result.Driver);
      Assert.Equal("local", result.Scope);
      Assert.False(result.IPv6); // defaults
      Assert.False(result.Internal);
    }

    /// <summary>
    /// Helper to create ProcessExecutionResult using reflection (constructor is internal).
    /// </summary>
    private static ProcessExecutionResult CreateProcessExecutionResult(
        string command, string stdOut, string stdErr, int exitCode)
    {
      var ctorArgs = new object[] { command, stdOut, stdErr, exitCode };
      var result = (ProcessExecutionResult)Activator.CreateInstance(
          typeof(ProcessExecutionResult),
          BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.CreateInstance,
          null, ctorArgs, null, null);

      return result;
    }
  }
}

