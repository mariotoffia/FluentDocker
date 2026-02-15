using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  public class DriverSelectionTests
  {
    [Fact]
    public void Default_HasUseDefaultTrue()
    {
      var selection = DriverSelection.Default;
      Assert.True(selection.UseDefault);
      Assert.Null(selection.DriverId);
      Assert.Null(selection.ExpectedType);
    }

    [Fact]
    public void Specific_SetsDriverIdAndUseDefaultFalse()
    {
      var selection = DriverSelection.Specific("my-driver");
      Assert.False(selection.UseDefault);
      Assert.Equal("my-driver", selection.DriverId);
      Assert.Null(selection.ExpectedType);
    }

    [Fact]
    public void Specific_WithExpectedType_SetsExpectedType()
    {
      var selection = DriverSelection.Specific("my-driver", DriverType.DockerCli);
      Assert.False(selection.UseDefault);
      Assert.Equal("my-driver", selection.DriverId);
      Assert.Equal(DriverType.DockerCli, selection.ExpectedType);
    }

    [Fact]
    public void DockerCli_UsesCorrectDefaults()
    {
      var selection = DriverSelection.DockerCli();
      Assert.False(selection.UseDefault);
      Assert.Equal("docker-cli", selection.DriverId);
      Assert.Equal(DriverType.DockerCli, selection.ExpectedType);
    }

    [Fact]
    public void DockerCli_CustomId_UsesProvidedId()
    {
      var selection = DriverSelection.DockerCli("my-docker");
      Assert.Equal("my-docker", selection.DriverId);
      Assert.Equal(DriverType.DockerCli, selection.ExpectedType);
    }

    [Fact]
    public void DockerApi_UsesCorrectDefaults()
    {
      var selection = DriverSelection.DockerApi();
      Assert.False(selection.UseDefault);
      Assert.Equal("docker-api", selection.DriverId);
      Assert.Equal(DriverType.DockerApi, selection.ExpectedType);
    }

    [Fact]
    public void PodmanCli_UsesCorrectDefaults()
    {
      var selection = DriverSelection.PodmanCli();
      Assert.False(selection.UseDefault);
      Assert.Equal("podman-cli", selection.DriverId);
      Assert.Equal(DriverType.PodmanCli, selection.ExpectedType);
    }
  }
}
