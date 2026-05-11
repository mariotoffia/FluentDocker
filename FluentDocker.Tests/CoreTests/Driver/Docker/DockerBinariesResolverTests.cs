using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Unit tests for DockerBinariesResolver binary resolution, sudo prefix handling,
  /// and path-based discovery logic.
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerBinariesResolverTests : IDisposable
  {
    private readonly string _tempDir;

    public DockerBinariesResolverTests()
    {
      _tempDir = Path.Combine(Path.GetTempPath(), $"fd_test_{Guid.NewGuid():N}");
      Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
      try
      {
        if (Directory.Exists(_tempDir))
          Directory.Delete(_tempDir, true);
      }
      catch
      {
        // best effort cleanup
      }

      GC.SuppressFinalize(this);
    }

    #region Helpers

    /// <summary>
    /// Creates a fake docker binary file in the temp directory.
    /// On non-Windows, creates a file named "docker"; on Windows, "docker.exe".
    /// </summary>
    private string CreateFakeDockerBinary()
    {
      var binaryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
          ? "docker.exe"
          : "docker";

      var filePath = Path.Combine(_tempDir, binaryName);
      File.WriteAllText(filePath, "fake-docker-binary");
      return filePath;
    }

    /// <summary>
    /// Creates a resolver pointing at the temp directory with a fake docker binary.
    /// </summary>
    private DockerBinariesResolver CreateResolverWithFakeBinary(
        SudoMechanism sudo = SudoMechanism.None,
        string password = null)
    {
      CreateFakeDockerBinary();
      return new DockerBinariesResolver(sudo, password, _tempDir);
    }

    /// <summary>
    /// Invokes the private ResolveFromPaths instance method via reflection.
    /// In v3 the method was changed from static → instance so it can use the
    /// resolver's ILogger field for parse-failure warnings. The test stages
    /// a throwaway directory containing a fake "docker" binary so the resolver
    /// ctor doesn't throw "client not found", then reflects against the instance.
    /// </summary>
    private static IEnumerable<DockerBinary> InvokeResolveFromPaths(
        SudoMechanism sudo, string password, params string[] paths)
    {
      var fakeDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
      Directory.CreateDirectory(fakeDir);
      try
      {
        var binaryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "docker.exe" : "docker";
        File.WriteAllText(Path.Combine(fakeDir, binaryName), "fake");

        var instance = new DockerBinariesResolver(new BinaryConfiguration
        {
          SearchPaths = [fakeDir]
        });
        var method = typeof(DockerBinariesResolver).GetMethod(
            "ResolveFromPaths",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var result = method.Invoke(instance, [sudo, password, paths]);
        return (IEnumerable<DockerBinary>)result;
      }
      finally
      {
        try
        { Directory.Delete(fakeDir, recursive: true); }
        catch { /* ignore */ }
      }
    }

    #endregion

    #region Constructor -- Custom Search Paths

    [Fact]
    public void Constructor_WithFakeDocker_FindsMainDockerClient()
    {
      var resolver = CreateResolverWithFakeBinary();

      Assert.NotNull(resolver.MainDockerClient);
      Assert.Equal(DockerBinaryType.DockerClient, resolver.MainDockerClient.Type);
    }

    [Fact]
    public void Constructor_WithFakeDocker_BinariesArrayContainsDockerClient()
    {
      var resolver = CreateResolverWithFakeBinary();

      Assert.Contains(resolver.Binaries,
          b => b.Type == DockerBinaryType.DockerClient);
    }

    [Fact]
    public void Constructor_WithFakeDocker_PathMatchesTempDir()
    {
      var resolver = CreateResolverWithFakeBinary();

      Assert.Equal(_tempDir, resolver.MainDockerClient.Path);
    }

    [Fact]
    public void Constructor_WithSudoNoPassword_PropagatesSudoToResolvedBinary()
    {
      var resolver = CreateResolverWithFakeBinary(SudoMechanism.NoPassword);

      Assert.Equal(SudoMechanism.NoPassword, resolver.MainDockerClient.Sudo);
    }

    [Fact]
    public void Constructor_WithSudoPassword_PropagatesSudoAndPasswordToResolvedBinary()
    {
      var resolver = CreateResolverWithFakeBinary(SudoMechanism.Password, "pass123");

      Assert.Equal(SudoMechanism.Password, resolver.MainDockerClient.Sudo);
      Assert.Equal("pass123", resolver.MainDockerClient.SudoPassword);
    }

    #endregion

    #region Constructor -- No Docker Found

    [Fact]
    public void Constructor_NoDockerInSearchPath_ThrowsFluentDockerException()
    {
      // The temp directory exists but has no docker binary
      Assert.Throws<FluentDockerException>(() =>
          new DockerBinariesResolver(SudoMechanism.None, null, _tempDir));
    }

    [Fact]
    public void Constructor_EmptyDirectory_ThrowsFluentDockerException()
    {
      var emptyDir = Path.Combine(_tempDir, "empty");
      Directory.CreateDirectory(emptyDir);

      Assert.Throws<FluentDockerException>(() =>
          new DockerBinariesResolver(SudoMechanism.None, null, emptyDir));
    }

    [Fact]
    public void Constructor_NonExistentDirectory_ThrowsFluentDockerException()
    {
      var nonExistent = Path.Combine(_tempDir, "does_not_exist");

      Assert.Throws<FluentDockerException>(() =>
          new DockerBinariesResolver(SudoMechanism.None, null, nonExistent));
    }

    #endregion

    #region Constructor -- BinaryConfiguration Overload

    [Fact]
    public void Constructor_WithBinaryConfiguration_UsesSearchPaths()
    {
      CreateFakeDockerBinary();
      var config = new BinaryConfiguration
      {
        Sudo = SudoMechanism.None,
        SearchPaths = [_tempDir]
      };

      var resolver = new DockerBinariesResolver(config);

      Assert.NotNull(resolver.MainDockerClient);
      Assert.Equal(_tempDir, resolver.MainDockerClient.Path);
    }

    [Fact]
    public void Constructor_NullConfiguration_UsesDefaults()
    {
      // Passing null should create a default BinaryConfiguration and
      // search PATH. If docker is not on PATH, it throws.
      // We cannot guarantee docker is installed, so we just verify it
      // does not throw NullReferenceException.
      try
      {
        var _ = new DockerBinariesResolver(null);
        // If docker is installed, this succeeds
      }
      catch (FluentDockerException)
      {
        // Expected when docker is not on PATH -- not a NullReferenceException
      }
    }

    #endregion

    #region Resolve

    [Fact]
    public void Resolve_Docker_ReturnsMainDockerClient()
    {
      var resolver = CreateResolverWithFakeBinary();

      var binary = resolver.Resolve("docker");

      Assert.NotNull(binary);
      Assert.Equal(DockerBinaryType.DockerClient, binary.Type);
    }

    [Fact]
    public void Resolve_UnknownBinary_ThrowsArgumentException()
    {
      var resolver = CreateResolverWithFakeBinary();

      // DockerBinary.Translate throws ArgumentException for unknown names
      Assert.Throws<ArgumentException>(() => resolver.Resolve("podman"));
    }

    #endregion

    #region ResolveBinaryPath

    [Fact]
    public void ResolveBinaryPath_NoSudo_ReturnsFqPath()
    {
      var resolver = CreateResolverWithFakeBinary(SudoMechanism.None);

      var path = resolver.ResolveBinaryPath("docker");

      Assert.Equal(resolver.MainDockerClient.FqPath, path);
    }

    [Fact]
    public void ResolveBinaryPath_SudoNoPassword_ReturnsSudoPrefix()
    {
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return; // Sudo prefix is not applied on Windows

      var resolver = CreateResolverWithFakeBinary(SudoMechanism.NoPassword);

      var path = resolver.ResolveBinaryPath("docker");

      Assert.StartsWith("sudo ", path);
      Assert.Contains(resolver.MainDockerClient.FqPath, path);
      Assert.DoesNotContain("-S", path);
    }

    [Fact]
    public void ResolveBinaryPath_SudoPassword_ReturnsSudoDashSPrefix()
    {
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return; // Sudo prefix is not applied on Windows

      var resolver = CreateResolverWithFakeBinary(SudoMechanism.Password, "secret");

      var path = resolver.ResolveBinaryPath("docker");

      Assert.StartsWith("sudo -S ", path);
      Assert.Contains(resolver.MainDockerClient.FqPath, path);
      // Password should never appear in the path string
      Assert.DoesNotContain("secret", path);
    }

    #endregion

    #region IsDockerComposeAvailable

    [Fact]
    public void IsDockerComposeAvailable_WhenComposeIsNull_ReturnsFalse()
    {
      // Create a resolver with a fake docker that does not support compose.
      // CheckCompose will fail since the fake binary is not executable.
      var resolver = CreateResolverWithFakeBinary();

      // The fake binary cannot run "docker compose version",
      // so MainDockerCompose should be null.
      Assert.False(resolver.IsDockerComposeAvailable);
    }

    #endregion

    #region ResolveFromPaths (private static via reflection)

    [Fact]
    public void ResolveFromPaths_EmptyDirectory_ReturnsEmpty()
    {
      var emptyDir = Path.Combine(_tempDir, "empty_sub");
      Directory.CreateDirectory(emptyDir);

      var binaries = InvokeResolveFromPaths(
          SudoMechanism.None, null, emptyDir);

      Assert.Empty(binaries);
    }

    [Fact]
    public void ResolveFromPaths_NonExistentPath_ReturnsEmpty()
    {
      var binaries = InvokeResolveFromPaths(
          SudoMechanism.None, null,
          Path.Combine(_tempDir, "nonexistent"));

      Assert.Empty(binaries);
    }

    [Fact]
    public void ResolveFromPaths_DirectoryWithDockerBinary_FindsIt()
    {
      CreateFakeDockerBinary();

      var binaries = InvokeResolveFromPaths(
          SudoMechanism.None, null, _tempDir).ToList();

      Assert.Single(binaries);
      Assert.Equal(DockerBinaryType.DockerClient, binaries[0].Type);
    }

    [Fact]
    public void ResolveFromPaths_WithSudo_PropagatesSudoToBinaries()
    {
      CreateFakeDockerBinary();

      var binaries = InvokeResolveFromPaths(
          SudoMechanism.Password, "pwd123", _tempDir).ToList();

      Assert.Single(binaries);
      Assert.Equal(SudoMechanism.Password, binaries[0].Sudo);
      Assert.Equal("pwd123", binaries[0].SudoPassword);
    }

    [Fact]
    public void ResolveFromPaths_MultiplePaths_SearchesAll()
    {
      CreateFakeDockerBinary();

      var secondDir = Path.Combine(_tempDir, "second");
      Directory.CreateDirectory(secondDir);

      var binaries = InvokeResolveFromPaths(
          SudoMechanism.None, null, _tempDir, secondDir).ToList();

      // Only the first directory has docker; second is empty
      Assert.Single(binaries);
    }

    [Fact]
    public void ResolveFromPaths_IgnoresNonDockerFiles()
    {
      CreateFakeDockerBinary();

      // Create unrelated files that start with "docker" prefix
      // but are not "docker" or "docker.exe"
      File.WriteAllText(Path.Combine(_tempDir, "docker-garbage"), "not docker");
      File.WriteAllText(Path.Combine(_tempDir, "dockerfoo"), "not docker");

      var binaries = InvokeResolveFromPaths(
          SudoMechanism.None, null, _tempDir).ToList();

      // Should only find the real "docker" binary
      Assert.Single(binaries);
      Assert.Equal(DockerBinaryType.DockerClient, binaries[0].Type);
    }

    [Fact]
    public void ResolveFromPaths_NullPaths_FallsBackToEnvPath()
    {
      // When paths is null, should use PATH env variable.
      // We cannot control what PATH contains, but we can verify it does not throw.
      var binaries = InvokeResolveFromPaths(SudoMechanism.None, null, null);
      Assert.NotNull(binaries);
    }

    [Fact]
    public void ResolveFromPaths_EmptyArrayPaths_FallsBackToEnvPath()
    {
      var binaries = InvokeResolveFromPaths(
          SudoMechanism.None, null, []);
      Assert.NotNull(binaries);
    }

    #endregion

    #region BinaryConfiguration

    [Fact]
    public void BinaryConfiguration_DefaultValues()
    {
      var config = new BinaryConfiguration();

      Assert.Equal(SudoMechanism.None, config.Sudo);
      Assert.Null(config.SudoPassword);
      Assert.Equal("bash", config.DefaultShell);
      Assert.Null(config.SearchPaths);
    }

    [Fact]
    public void BinaryConfiguration_ConstructorWithSudo()
    {
      var config = new BinaryConfiguration(SudoMechanism.Password, "test");

      Assert.Equal(SudoMechanism.Password, config.Sudo);
      Assert.Equal("test", config.SudoPassword);
    }

    [Fact]
    public void BinaryConfiguration_SettableProperties()
    {
      var config = new BinaryConfiguration
      {
        Sudo = SudoMechanism.NoPassword,
        SudoPassword = "pw",
        DefaultShell = "zsh",
        SearchPaths = ["/usr/bin", "/usr/local/bin"]
      };

      Assert.Equal(SudoMechanism.NoPassword, config.Sudo);
      Assert.Equal("pw", config.SudoPassword);
      Assert.Equal("zsh", config.DefaultShell);
      Assert.Equal(2, config.SearchPaths.Length);
    }

    #endregion

    #region MainDockerCli Property

    [Fact]
    public void MainDockerCli_WhenNoCli_ReturnsNull()
    {
      // The fake docker binary is "docker" (DockerClient), not "dockercli"
      var resolver = CreateResolverWithFakeBinary();

      Assert.Null(resolver.MainDockerCli);
    }

    #endregion
  }
}
