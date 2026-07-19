using System;
using System.Collections.Generic;
using FluentDocker.Model.Images;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  [Trait("Category", "Unit")]
  public class ImageModelTests
  {
    #region ImageRemovalOption Enum

    [Fact]
    public void ImageRemovalOption_DefaultValue_IsNone()
    {
      // Arrange & Act
      var value = default(ImageRemovalOption);

      // Assert
      Assert.Equal(ImageRemovalOption.None, value);
    }

    [Theory]
    [InlineData(ImageRemovalOption.None, 0)]
    [InlineData(ImageRemovalOption.Local, 1)]
    [InlineData(ImageRemovalOption.All, 2)]
    public void ImageRemovalOption_EnumValues_HaveExpectedIntValues(
      ImageRemovalOption option, int expected)
    {
      // Assert
      Assert.Equal(expected, (int)option);
    }

    [Fact]
    public void ImageRemovalOption_HasExactlyThreeValues()
    {
      // Arrange & Act
      var values = Enum.GetValues<ImageRemovalOption>();

      // Assert
      Assert.Equal(3, values.Length);
    }

    #endregion

    #region DockerImageRowResponse

    [Fact]
    public void DockerImageRowResponse_DefaultConstruction_AllPropertiesAreNull()
    {
      // Arrange & Act
      var response = new DockerImageRowResponse();

      // Assert
      Assert.Null(response.Id);
      Assert.Null(response.Name);
      Assert.Null(response.Tags);
    }

    [Fact]
    public void DockerImageRowResponse_SetAllProperties_ValuesAreRetained()
    {
      // Arrange & Act
      var response = new DockerImageRowResponse
      {
        Id = "sha256:abc123",
        Name = "nginx",
        Tags = ["latest", "1.25", "1.25.3"]
      };

      // Assert
      Assert.Equal("sha256:abc123", response.Id);
      Assert.Equal("nginx", response.Name);
      Assert.Equal(3, response.Tags.Length);
      Assert.Equal("latest", response.Tags[0]);
      Assert.Equal("1.25", response.Tags[1]);
      Assert.Equal("1.25.3", response.Tags[2]);
    }

    [Fact]
    public void DockerImageRowResponse_EmptyTags_IsEmptyArray()
    {
      // Arrange & Act
      var response = new DockerImageRowResponse
      {
        Id = "sha256:def456",
        Name = "custom-image",
        Tags = []
      };

      // Assert
      Assert.NotNull(response.Tags);
      Assert.Empty(response.Tags);
    }

    [Fact]
    public void DockerImageRowResponse_SingleTag_HasOneElement()
    {
      // Arrange & Act
      var response = new DockerImageRowResponse
      {
        Id = "sha256:aaa",
        Name = "alpine",
        Tags = ["3.19"]
      };

      // Assert
      Assert.Single(response.Tags);
      Assert.Equal("3.19", response.Tags[0]);
    }

    #endregion

    #region DockerRmImageRowResponse

    [Fact]
    public void DockerRmImageRowResponse_DefaultConstruction_AllPropertiesAreNull()
    {
      // Arrange & Act
      var response = new DockerRmImageRowResponse();

      // Assert
      Assert.Null(response.Id);
      Assert.Null(response.Command);
    }

    [Fact]
    public void DockerRmImageRowResponse_SetAllProperties_ValuesAreRetained()
    {
      // Arrange & Act
      var response = new DockerRmImageRowResponse
      {
        Id = "sha256:deadbeef",
        Command = "Untagged"
      };

      // Assert
      Assert.Equal("sha256:deadbeef", response.Id);
      Assert.Equal("Untagged", response.Command);
    }

    [Theory]
    [InlineData("Untagged")]
    [InlineData("Deleted")]
    public void DockerRmImageRowResponse_TypicalCommands_AreStoredCorrectly(string command)
    {
      // Arrange & Act
      var response = new DockerRmImageRowResponse
      {
        Id = "sha256:123",
        Command = command
      };

      // Assert
      Assert.Equal(command, response.Command);
    }

    #endregion

  }
}
