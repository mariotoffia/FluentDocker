using System;
using System.Reflection;
using FluentDocker.Drivers.Docker.Api.Connection;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public sealed class DockerApiConnectionNamedPipeTests
  {
    // ponytail: Mock-only npipe coverage should be backed by a Windows Docker smoke tier when CI has one.
    [Theory]
    [InlineData("npipe:////./pipe/docker_engine", "docker_engine")]
    [InlineData("npipe://./pipe/docker_engine", "docker_engine")]
    [InlineData("npipe:////./pipe/custom_engine", "custom_engine")]
    [InlineData("npipe://./pipe/foo/bar", @"foo\bar")]
    public void ExtractNamedPipeName_ReturnsPipePathAfterPipeNamespace(string host, string expected)
    {
      var method = typeof(DockerApiConnection).GetMethod(
          "ExtractNamedPipeName",
          BindingFlags.Static | BindingFlags.NonPublic);
      Assert.NotNull(method);

      var pipeName = (string)method.Invoke(null, [new Uri(host)])!;

      Assert.Equal(expected, pipeName);
    }

    [Fact]
    public void Constructor_RemoteNamedPipeHost_ThrowsArgumentException()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "npipe://server/pipe/docker_engine",
        ApiVersion = "1.45"
      };

      var error = Assert.Throws<ArgumentException>(() => new DockerApiConnection(config));
      Assert.Contains("Remote Docker named pipes are not supported", error.Message);
    }
  }
}
