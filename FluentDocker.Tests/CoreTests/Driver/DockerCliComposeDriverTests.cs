using System.Collections.Generic;
using System.Reflection;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Docker.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Unit tests for DockerCliComposeDriver argument quoting.
  /// Validates that paths and names with spaces are properly quoted.
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerCliComposeDriverTests
  {
    #region QuoteArgumentIfNeeded (base class)

    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("", "")]
    [InlineData(null, null)]
    [InlineData("no-spaces", "no-spaces")]
    [InlineData("/usr/local/bin/docker", "/usr/local/bin/docker")]
    public void QuoteArgumentIfNeeded_NoSpaces_ReturnsUnchanged(string input, string expected)
    {
      var result = InvokeQuoteArgumentIfNeeded(input);
      Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("hello world", "\"hello world\"")]
    [InlineData("/my path/to/file.yml", "\"/my path/to/file.yml\"")]
    [InlineData("my project", "\"my project\"")]
    [InlineData("C:\\Program Files\\Docker", "\"C:\\\\Program Files\\\\Docker\"")]
    [InlineData("path with\ttab", "\"path with\ttab\"")]
    public void QuoteArgumentIfNeeded_WithSpaces_ReturnsQuoted(string input, string expected)
    {
      var result = InvokeQuoteArgumentIfNeeded(input);
      Assert.Equal(expected, result);
    }

    [Fact]
    public void QuoteArgumentIfNeeded_WithQuotesAndSpaces_EscapesBoth()
    {
      var result = InvokeQuoteArgumentIfNeeded("my \"quoted\" path");
      Assert.Equal("\"my \\\"quoted\\\" path\"", result);
    }

    #endregion

    #region BuildComposeArgs

    [Fact]
    public void BuildComposeArgs_SimpleValues_NoQuoting()
    {
      var config = new ComposeFileConfig
      {
        ComposeFiles = new List<string> { "docker-compose.yml" },
        ProjectName = "myproject"
      };

      var result = InvokeBuildComposeArgs(config);

      Assert.Equal("compose -f docker-compose.yml -p myproject", result);
    }

    [Fact]
    public void BuildComposeArgs_FileWithSpaces_Quoted()
    {
      var config = new ComposeFileConfig
      {
        ComposeFiles = new List<string> { "/my path/docker-compose.yml" },
        ProjectName = "myproject"
      };

      var result = InvokeBuildComposeArgs(config);

      Assert.Contains("-f \"/my path/docker-compose.yml\"", result);
    }

    [Fact]
    public void BuildComposeArgs_ProjectNameWithSpaces_Quoted()
    {
      var config = new ComposeFileConfig
      {
        ComposeFiles = new List<string> { "docker-compose.yml" },
        ProjectName = "my project"
      };

      var result = InvokeBuildComposeArgs(config);

      Assert.Contains("-p \"my project\"", result);
    }

    [Fact]
    public void BuildComposeArgs_ProjectDirectoryWithSpaces_Quoted()
    {
      var config = new ComposeFileConfig
      {
        ComposeFiles = new List<string> { "docker-compose.yml" },
        ProjectDirectory = "/home/user/my project"
      };

      var result = InvokeBuildComposeArgs(config);

      Assert.Contains("--project-directory \"/home/user/my project\"", result);
    }

    [Fact]
    public void BuildComposeArgs_MultipleFilesWithSpaces_AllQuoted()
    {
      var config = new ComposeFileConfig
      {
        ComposeFiles = new List<string>
                {
                    "/my path/docker-compose.yml",
                    "/my path/docker-compose.override.yml"
                },
        ProjectName = "my project",
        ProjectDirectory = "/my dir"
      };

      var result = InvokeBuildComposeArgs(config);

      Assert.Contains("-f \"/my path/docker-compose.yml\"", result);
      Assert.Contains("-f \"/my path/docker-compose.override.yml\"", result);
      Assert.Contains("-p \"my project\"", result);
      Assert.Contains("--project-directory \"/my dir\"", result);
    }

    [Fact]
    public void BuildComposeArgs_WindowsPathWithSpaces_QuotedAndEscaped()
    {
      var config = new ComposeFileConfig
      {
        ComposeFiles = new List<string> { "C:\\Program Files\\docker-compose.yml" },
        ProjectDirectory = "C:\\My Projects\\app"
      };

      var result = InvokeBuildComposeArgs(config);

      Assert.Contains("-f \"C:\\\\Program Files\\\\docker-compose.yml\"", result);
      Assert.Contains("--project-directory \"C:\\\\My Projects\\\\app\"", result);
    }

    #endregion

    #region Reflection Helpers

    private static string InvokeQuoteArgumentIfNeeded(string arg)
    {
      var method = typeof(DockerCliDriverBase).GetMethod(
          "QuoteArgumentIfNeeded",
          BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      return (string)method.Invoke(null, new object[] { arg });
    }

    private static string InvokeBuildComposeArgs(ComposeFileConfig config)
    {
      var driver = new DockerCliComposeDriver(null);
      var method = typeof(DockerCliComposeDriver).GetMethod(
          "BuildComposeArgs",
          BindingFlags.NonPublic | BindingFlags.Instance);
      Assert.NotNull(method);
      return (string)method.Invoke(driver, new object[] { config });
    }

    #endregion
  }
}
