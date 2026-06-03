using System;
using System.IO;
using System.Runtime.InteropServices;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Unit tests for resolving a docker-compatible CLI other than "docker"
  /// (issue #315): finch/nerdctl support via BinaryConfiguration.BinaryName.
  /// </summary>
  [Trait("Category", "Unit")]
  public sealed class DockerBinariesResolverCustomBinaryTests : IDisposable
  {
    private readonly string _tempDir;

    public DockerBinariesResolverCustomBinaryTests()
    {
      _tempDir = Path.Combine(Path.GetTempPath(), $"fd315_{Guid.NewGuid():N}");
      Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
      try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
      catch { /* best effort */ }
      GC.SuppressFinalize(this);
    }

    private string CreateFakeBinary(string name)
    {
      var file = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"{name}.exe" : name;
      var path = Path.Combine(_tempDir, file);
      File.WriteAllText(path, "fake");
      return path;
    }

    [Fact]
    public void CustomBinary_Finch_ResolvedAsMainDockerClient()
    {
      CreateFakeBinary("finch");

      var resolver = new DockerBinariesResolver(new BinaryConfiguration
      {
        BinaryName = "finch",
        SearchPaths = [_tempDir]
      });

      Assert.NotNull(resolver.MainDockerClient);
      Assert.Equal(DockerBinaryType.DockerClient, resolver.MainDockerClient.Type);
      Assert.Equal(_tempDir, resolver.MainDockerClient.Path);
      // ResolveBinaryPath("docker") returns the configured engine's binary.
      Assert.Contains("finch", resolver.ResolveBinaryPath("docker"));
    }

    [Fact]
    public void CustomBinary_Nerdctl_ResolvedAsMainDockerClient()
    {
      CreateFakeBinary("nerdctl");

      var resolver = new DockerBinariesResolver(new BinaryConfiguration
      {
        BinaryName = "nerdctl",
        SearchPaths = [_tempDir]
      });

      Assert.NotNull(resolver.MainDockerClient);
      Assert.Contains("nerdctl", resolver.MainDockerClient.FqPath);
    }

    [Fact]
    public void CustomBinary_WhenMissing_Throws()
    {
      // BinaryName is finch but only a docker binary is present -> not found.
      CreateFakeBinary("docker");

      Assert.Throws<FluentDocker.Common.FluentDockerException>(() =>
          new DockerBinariesResolver(new BinaryConfiguration
          {
            BinaryName = "finch",
            SearchPaths = [_tempDir]
          }));
    }

    [Fact]
    public void DefaultBinaryName_IsDocker()
    {
      Assert.Equal("docker", new BinaryConfiguration().BinaryName);
    }

    [Fact]
    public void Docker_StillResolves_WhenBinaryNameDefaulted()
    {
      CreateFakeBinary("docker");

      var resolver = new DockerBinariesResolver(new BinaryConfiguration
      {
        SearchPaths = [_tempDir] // BinaryName defaults to "docker"
      });

      Assert.NotNull(resolver.MainDockerClient);
      Assert.Equal(DockerBinaryType.DockerClient, resolver.MainDockerClient.Type);
    }
  }
}
