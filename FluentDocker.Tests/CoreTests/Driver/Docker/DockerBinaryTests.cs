using System;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Unit tests for DockerBinary construction, property mapping, and Translate logic.
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerBinaryTests
  {
    #region Constructor -- Basic Properties

    [Fact]
    public void Constructor_SetsPathProperty()
    {
      var binary = new DockerBinary("/usr/bin", "docker", SudoMechanism.None, null);
      Assert.Equal("/usr/bin", binary.Path);
    }

    [Fact]
    public void Constructor_SetsBinaryProperty_LowerCased()
    {
      var binary = new DockerBinary("/usr/bin", "Docker", SudoMechanism.None, null);
      Assert.Equal("docker", binary.Binary);
    }

    [Fact]
    public void Constructor_SetsSudoProperty()
    {
      var binary = new DockerBinary("/usr/bin", "docker", SudoMechanism.NoPassword, null);
      Assert.Equal(SudoMechanism.NoPassword, binary.Sudo);
    }

    [Fact]
    public void Constructor_SetsSudoPasswordProperty()
    {
      var binary = new DockerBinary(
          "/usr/bin", "docker", SudoMechanism.Password, "secret123");
      Assert.Equal("secret123", binary.SudoPassword);
    }

    [Fact]
    public void Constructor_SetsTypeViaTranslate()
    {
      var binary = new DockerBinary("/usr/bin", "docker", SudoMechanism.None, null);
      Assert.Equal(DockerBinaryType.DockerClient, binary.Type);
    }

    [Fact]
    public void Constructor_AllProperties()
    {
      var binary = new DockerBinary(
          "/usr/local/bin", "docker", SudoMechanism.Password, "pass456");

      Assert.Equal("/usr/local/bin", binary.Path);
      Assert.Equal("docker", binary.Binary);
      Assert.Equal(DockerBinaryType.DockerClient, binary.Type);
      Assert.Equal(SudoMechanism.Password, binary.Sudo);
      Assert.Equal("pass456", binary.SudoPassword);
    }

    #endregion

    #region Constructor -- Explicit Type Override

    [Fact]
    public void Constructor_WithExplicitType_OverridesTranslation()
    {
      var binary = new DockerBinary(
          "/usr/bin", "docker", SudoMechanism.None, null, DockerBinaryType.Compose);

      Assert.Equal(DockerBinaryType.Compose, binary.Type);
    }

    [Fact]
    public void Constructor_WithExplicitType_StillNormalizesBinary()
    {
      var binary = new DockerBinary(
          "/usr/bin", "DOCKER", SudoMechanism.None, null, DockerBinaryType.Cli);

      Assert.Equal("docker", binary.Binary);
      Assert.Equal(DockerBinaryType.Cli, binary.Type);
    }

    #endregion

    #region FqPath

    [Fact]
    public void FqPath_CombinesPathAndBinary()
    {
      var binary = new DockerBinary("/usr/bin", "docker", SudoMechanism.None, null);
      Assert.Equal(System.IO.Path.Combine("/usr/bin", "docker"), binary.FqPath);
    }

    [Fact]
    public void FqPath_WithTrailingSlash_CombinesCorrectly()
    {
      // System.IO.Path.Combine handles trailing separators
      var binary = new DockerBinary("/usr/bin/", "docker", SudoMechanism.None, null);
      var expected = System.IO.Path.Combine("/usr/bin/", "docker");
      Assert.Equal(expected, binary.FqPath);
    }

    #endregion

    #region Translate -- Known Binaries

    [Fact]
    public void Translate_Docker_ReturnsDockerClient()
    {
      var type = DockerBinary.Translate("docker");
      Assert.Equal(DockerBinaryType.DockerClient, type);
    }

    [Fact]
    public void Translate_DockerExe_ReturnsDockerClient()
    {
      var type = DockerBinary.Translate("docker.exe");
      Assert.Equal(DockerBinaryType.DockerClient, type);
    }

    [Fact]
    public void Translate_DockerCli_ReturnsCli()
    {
      var type = DockerBinary.Translate("dockercli");
      Assert.Equal(DockerBinaryType.Cli, type);
    }

    [Fact]
    public void Translate_DockerCliExe_ReturnsCli()
    {
      var type = DockerBinary.Translate("dockercli.exe");
      Assert.Equal(DockerBinaryType.Cli, type);
    }

    [Fact]
    public void Translate_Compose_ReturnsCompose()
    {
      var type = DockerBinary.Translate("compose");
      Assert.Equal(DockerBinaryType.Compose, type);
    }

    [Fact]
    public void Translate_DockerUpperCase_ReturnsDockerClient()
    {
      // Translate converts to lower before matching
      var type = DockerBinary.Translate("DOCKER");
      Assert.Equal(DockerBinaryType.DockerClient, type);
    }

    [Fact]
    public void Translate_DockerMixedCase_ReturnsDockerClient()
    {
      var type = DockerBinary.Translate("Docker");
      Assert.Equal(DockerBinaryType.DockerClient, type);
    }

    #endregion

    #region Translate -- Unknown / Legacy Binaries

    [Fact]
    public void Translate_Unknown_ThrowsArgumentException()
    {
      Assert.Throws<ArgumentException>(() => DockerBinary.Translate("podman"));
    }

    [Fact]
    public void Translate_DockerComposeLegacy_ThrowsArgumentException()
    {
      // docker-compose (standalone) is no longer supported in v3.0
      Assert.Throws<ArgumentException>(() => DockerBinary.Translate("docker-compose"));
    }

    [Fact]
    public void Translate_DockerMachine_ThrowsArgumentException()
    {
      // docker-machine is no longer supported in v3.0
      Assert.Throws<ArgumentException>(() => DockerBinary.Translate("docker-machine"));
    }

    [Fact]
    public void Translate_EmptyString_ThrowsArgumentException()
    {
      Assert.Throws<ArgumentException>(() => DockerBinary.Translate(""));
    }

    #endregion

    #region SudoMechanism Values

    [Fact]
    public void SudoMechanism_None_IsDefault()
    {
      var binary = new DockerBinary("/usr/bin", "docker", SudoMechanism.None, null);
      Assert.Equal(SudoMechanism.None, binary.Sudo);
      Assert.Null(binary.SudoPassword);
    }

    [Fact]
    public void SudoMechanism_NoPassword_SetsCorrectly()
    {
      var binary = new DockerBinary(
          "/usr/bin", "docker", SudoMechanism.NoPassword, null);
      Assert.Equal(SudoMechanism.NoPassword, binary.Sudo);
    }

    [Fact]
    public void SudoMechanism_Password_SetsPasswordCorrectly()
    {
      var binary = new DockerBinary(
          "/usr/bin", "docker", SudoMechanism.Password, "mypassword");
      Assert.Equal(SudoMechanism.Password, binary.Sudo);
      Assert.Equal("mypassword", binary.SudoPassword);
    }

    #endregion
  }
}
