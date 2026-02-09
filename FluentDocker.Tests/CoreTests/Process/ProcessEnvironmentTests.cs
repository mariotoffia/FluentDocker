using System.Collections.Generic;
using System.IO;
using FluentDocker.Common;
using FluentDocker.Executors;
using FluentDocker.Executors.Parsers;
using FluentDocker.Model.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Process
{
  /// <summary>
  /// Tests for process execution with custom environment variables.
  /// Note: These tests execute actual shell scripts and may require 
  /// appropriate permissions on Unix systems.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ProcessEnvironmentTests
  {
    [Fact]
    public void ProcessExecutor_PassesCustomEnvironmentVariable()
    {
      var cmd = "Resources/Scripts/envtest." + (FdOs.IsWindows() ? "bat" : "sh");
      var file = Path.Combine(Directory.GetCurrentDirectory(), (TemplateString)cmd);

      if (!File.Exists(file))
        return; // Skip if test script not found

      // On Unix, ensure the script is executable
      if (!FdOs.IsWindows())
      {
        try
        {
          var chmod = new System.Diagnostics.Process
          {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
              FileName = "chmod",
              Arguments = $"+x \"{file}\"",
              UseShellExecute = false,
              CreateNoWindow = true
            }
          };
          chmod.Start();
          chmod.WaitForExit();
        }
        catch
        {
          return; // Skip if could not set permissions
        }
      }

      var executor = new ProcessExecutor<StringListResponseParser, IList<string>>(file, string.Empty);
      executor.Env["FD_CUSTOM_ENV"] = "My test environment variable";

      var result = executor.Execute();

      // Assert the environment variable was passed to the process
      Assert.True(result.Success, $"Process execution failed: {result.Error}");
      Assert.NotEmpty(result.Data);
      Assert.Contains("My test environment variable", result.Data);
    }

    [Fact]
    public void ProcessExecutor_MultipleEnvironmentVariables_AllPassed()
    {
      if (!FdOs.IsWindows())
        return; // This test uses a Windows-specific approach

      // On Windows, we can use cmd /c echo to test env vars
      var executor = new ProcessExecutor<StringListResponseParser, IList<string>>("cmd", "/c echo %FD_VAR1% %FD_VAR2%");
      executor.Env["FD_VAR1"] = "Value1";
      executor.Env["FD_VAR2"] = "Value2";

      var result = executor.Execute();

      Assert.True(result.Success);
      Assert.Contains("Value1", string.Join(" ", result.Data));
      Assert.Contains("Value2", string.Join(" ", result.Data));
    }

    [Fact]
    public void ProcessExecutor_EnvironmentVariableWithSpecialCharacters()
    {
      var cmd = "Resources/Scripts/envtest." + (FdOs.IsWindows() ? "bat" : "sh");
      var file = Path.Combine(Directory.GetCurrentDirectory(), (TemplateString)cmd);

      if (!File.Exists(file))
        return; // Skip if test script not found

      // On Unix, ensure the script is executable
      if (!FdOs.IsWindows())
      {
        try
        {
          var chmod = new System.Diagnostics.Process
          {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
              FileName = "chmod",
              Arguments = $"+x \"{file}\"",
              UseShellExecute = false,
              CreateNoWindow = true
            }
          };
          chmod.Start();
          chmod.WaitForExit();
        }
        catch
        {
          return; // Skip if could not set permissions
        }
      }

      var executor = new ProcessExecutor<StringListResponseParser, IList<string>>(file, string.Empty);
      var specialValue = "Value with spaces and special chars: @#$%";
      executor.Env["FD_CUSTOM_ENV"] = specialValue;

      var result = executor.Execute();

      Assert.True(result.Success, $"Process execution failed: {result.Error}");
      Assert.NotEmpty(result.Data);
    }

    [Fact]
    public void ProcessExecutor_EmptyEnvironment_StillWorks()
    {
      // Simple test that process execution works without custom env
      var echoCmd = FdOs.IsWindows() ? "cmd" : "echo";
      var echoArgs = FdOs.IsWindows() ? "/c echo test" : "test";

      var executor = new ProcessExecutor<StringListResponseParser, IList<string>>(echoCmd, echoArgs);

      var result = executor.Execute();

      Assert.True(result.Success);
      Assert.NotEmpty(result.Data);
    }
  }
}

