using System;
using FluentDocker.Common;
using FluentDocker.Model.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  [Trait("Category", "Unit")]
  public class DockerUriTests
  {
    [Fact]
    public void Constructor_UnixSocket_IsStandardDaemonTrue()
    {
      // Arrange & Act
      var uri = new DockerUri("unix:///var/run/docker.sock");

      // Assert
      Assert.True(uri.IsStandardDaemon);
    }

    [Fact]
    public void Constructor_NamedPipe_IsStandardDaemonTrue()
    {
      // Arrange & Act
      var uri = new DockerUri("npipe://./pipe/docker_engine");

      // Assert
      Assert.True(uri.IsStandardDaemon);
    }

    [Fact]
    public void Constructor_TcpUri_IsStandardDaemonFalse()
    {
      // Arrange & Act
      var uri = new DockerUri("tcp://192.168.1.1:2375");

      // Assert
      Assert.False(uri.IsStandardDaemon);
    }

    [Fact]
    public void Constructor_SshUri_IsStandardDaemonFalse()
    {
      // Arrange & Act
      var uri = new DockerUri("ssh://user@host");

      // Assert
      Assert.False(uri.IsStandardDaemon);
    }

    [Fact]
    public void Constructor_CustomUnixSocket_IsStandardDaemonFalse()
    {
      // Arrange & Act
      var uri = new DockerUri("unix:///custom/path.sock");

      // Assert - only the exact standard path is considered standard
      Assert.False(uri.IsStandardDaemon);
    }

    [Fact]
    public void ToString_UnixScheme_ReturnsBaseString()
    {
      // Arrange
      var uri = new DockerUri("unix:///var/run/docker.sock");

      // Act
      var result = uri.ToString();

      // Assert - unix scheme is not ssh or npipe, so it returns the base Uri string
      Assert.Equal("unix:///var/run/docker.sock", result);
    }

    [Fact]
    public void ToString_SshScheme_TrimsTrailingSlash()
    {
      // Arrange
      var uri = new DockerUri("ssh://user@host");

      // Act
      var result = uri.ToString();

      // Assert - ssh scheme trims trailing slash
      Assert.False(result.EndsWith('/'));
      Assert.Equal("ssh://user@host", result);
    }

    [Fact]
    public void ToString_NpipeScheme_InsertsDoubleSlash()
    {
      // Arrange
      var uri = new DockerUri("npipe://./pipe/docker_engine");

      // Act
      var result = uri.ToString();

      // Assert - the override inserts "//" at position 6 of base.ToString().
      // On macOS/Linux, base.ToString() returns "npipe://./pipe/docker_engine",
      // so the insertion produces "npipe:" + "//" + "//./pipe/docker_engine"
      // = "npipe:////./pipe/docker_engine". On Windows the base class may strip
      // the authority slashes, and the insertion restores them.
      Assert.StartsWith("npipe:", result);
      Assert.Contains("./pipe/docker_engine", result);

      // Verify the "//" insertion is present at position 6
      Assert.Equal("//", result.Substring(6, 2));
    }

    [Fact]
    public void ToString_TcpScheme_ReturnsBaseString()
    {
      // Arrange
      var uri = new DockerUri("tcp://192.168.1.1:2375");

      // Act
      var result = uri.ToString();

      // Assert - tcp is not ssh or npipe, so returns base string
      Assert.Contains("tcp://", result);
      Assert.Contains("192.168.1.1", result);
      Assert.Contains("2375", result);
    }

    [Fact]
    public void GetDockerHostEnvironmentPathOrDefault_NoEnvVar_ReturnsPlatformDefault()
    {
      // Save original value
      var original = Environment.GetEnvironmentVariable("DOCKER_HOST");

      try
      {
        // Arrange - ensure DOCKER_HOST is not set
        Environment.SetEnvironmentVariable("DOCKER_HOST", null);

        // Act
        var result = DockerUri.GetDockerHostEnvironmentPathOrDefault();

        // Assert - on macOS/Linux returns the unix socket path
        if (!FdOs.IsWindows())
          Assert.Equal("unix:///var/run/docker.sock", result);
        else
          Assert.Equal("npipe://./pipe/docker_engine", result);
      }
      finally
      {
        // Restore
        Environment.SetEnvironmentVariable("DOCKER_HOST", original);
      }
    }

    [Fact]
    public void GetDockerHostEnvironmentPathOrDefault_WithEnvVar_ReturnsEnvValue()
    {
      // Save original value
      var original = Environment.GetEnvironmentVariable("DOCKER_HOST");

      try
      {
        // Arrange
        var customHost = "tcp://my-custom-host:2375";
        Environment.SetEnvironmentVariable("DOCKER_HOST", customHost);

        // Act
        var result = DockerUri.GetDockerHostEnvironmentPathOrDefault();

        // Assert
        Assert.Equal(customHost, result);
      }
      finally
      {
        // Restore
        Environment.SetEnvironmentVariable("DOCKER_HOST", original);
      }
    }

    [Fact]
    public void Constructor_InheritsUriProperties()
    {
      // Arrange & Act
      var uri = new DockerUri("tcp://192.168.1.1:2375");

      // Assert - verify properties inherited from Uri base class
      Assert.Equal("tcp", uri.Scheme);
      Assert.Equal("192.168.1.1", uri.Host);
      Assert.Equal(2375, uri.Port);
    }
  }
}
