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
        Tags = new[] { "latest", "1.25", "1.25.3" }
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
        Tags = Array.Empty<string>()
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
        Tags = new[] { "3.19" }
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

    #region Image

    [Fact]
    public void Image_DefaultConstruction_StringPropertiesAreNull()
    {
      // Arrange & Act
      var image = new Image();

      // Assert
      Assert.Null(image.Id);
      Assert.Null(image.Repository);
      Assert.Null(image.Os);
      Assert.Null(image.Architecture);
      Assert.Null(image.DockerVersion);
      Assert.Null(image.Author);
      Assert.Null(image.ParentId);
    }

    [Fact]
    public void Image_DefaultConstruction_NumericPropertiesAreZero()
    {
      // Arrange & Act
      var image = new Image();

      // Assert
      Assert.Equal(0L, image.Size);
      Assert.Equal(0L, image.VirtualSize);
      Assert.Equal(default, image.Created);
    }

    [Fact]
    public void Image_DefaultConstruction_CollectionsAreInitializedEmpty()
    {
      // Arrange & Act
      var image = new Image();

      // Assert
      Assert.NotNull(image.Tags);
      Assert.Empty(image.Tags);
      Assert.NotNull(image.Digests);
      Assert.Empty(image.Digests);
      Assert.NotNull(image.Labels);
      Assert.Empty(image.Labels);
    }

    [Fact]
    public void Image_SetAllProperties_ValuesAreRetained()
    {
      // Arrange
      var created = new DateTime(2026, 2, 20, 14, 30, 0, DateTimeKind.Utc);

      // Act
      var image = new Image
      {
        Id = "sha256:e4720093a3c1381245b53a5a51b417963b3c4472d3f47fc301930a4f3b17b869",
        Repository = "myregistry.com/myapp",
        Tags = new List<string> { "latest", "v2.1.0", "v2" },
        Digests = new List<string>
        {
          "sha256:abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890"
        },
        Created = created,
        Size = 142_000_000L,
        VirtualSize = 285_000_000L,
        Labels = new Dictionary<string, string>
        {
          ["maintainer"] = "dev@example.com",
          ["version"] = "2.1.0",
          ["org.opencontainers.image.source"] = "https://github.com/example/myapp"
        },
        Os = "linux",
        Architecture = "amd64",
        DockerVersion = "24.0.7",
        Author = "DevTeam",
        ParentId = "sha256:parent1234"
      };

      // Assert
      Assert.Equal(
        "sha256:e4720093a3c1381245b53a5a51b417963b3c4472d3f47fc301930a4f3b17b869",
        image.Id);
      Assert.Equal("myregistry.com/myapp", image.Repository);
      Assert.Equal(3, image.Tags.Count);
      Assert.Contains("latest", image.Tags);
      Assert.Contains("v2.1.0", image.Tags);
      Assert.Contains("v2", image.Tags);
      Assert.Single(image.Digests);
      Assert.Equal(created, image.Created);
      Assert.Equal(142_000_000L, image.Size);
      Assert.Equal(285_000_000L, image.VirtualSize);
      Assert.Equal(3, image.Labels.Count);
      Assert.Equal("dev@example.com", image.Labels["maintainer"]);
      Assert.Equal("linux", image.Os);
      Assert.Equal("amd64", image.Architecture);
      Assert.Equal("24.0.7", image.DockerVersion);
      Assert.Equal("DevTeam", image.Author);
      Assert.Equal("sha256:parent1234", image.ParentId);
    }

    [Fact]
    public void Image_TagsList_IsMutableAfterCreation()
    {
      // Arrange
      var image = new Image();

      // Act
      image.Tags.Add("latest");
      image.Tags.Add("v1.0.0");

      // Assert
      Assert.Equal(2, image.Tags.Count);
      Assert.Equal("latest", image.Tags[0]);
      Assert.Equal("v1.0.0", image.Tags[1]);
    }

    [Fact]
    public void Image_DigestsList_IsMutableAfterCreation()
    {
      // Arrange
      var image = new Image();

      // Act
      image.Digests.Add("sha256:aaa");
      image.Digests.Add("sha256:bbb");

      // Assert
      Assert.Equal(2, image.Digests.Count);
    }

    [Fact]
    public void Image_LabelsDictionary_IsMutableAfterCreation()
    {
      // Arrange
      var image = new Image();

      // Act
      image.Labels["key1"] = "value1";
      image.Labels["key2"] = "value2";

      // Assert
      Assert.Equal(2, image.Labels.Count);
      Assert.Equal("value1", image.Labels["key1"]);
    }

    [Fact]
    public void Image_LargeSize_HandledCorrectly()
    {
      // Arrange -- a very large image (10 GB)
      var image = new Image
      {
        Size = 10_737_418_240L,
        VirtualSize = 21_474_836_480L
      };

      // Assert
      Assert.Equal(10_737_418_240L, image.Size);
      Assert.Equal(21_474_836_480L, image.VirtualSize);
    }

    [Fact]
    public void Image_CollectionsCanBeReplaced_NewInstanceIsUsed()
    {
      // Arrange
      var image = new Image();
      image.Tags.Add("old-tag");

      // Act
      image.Tags = new List<string> { "new-tag" };

      // Assert
      Assert.Single(image.Tags);
      Assert.Equal("new-tag", image.Tags[0]);
    }

    [Theory]
    [InlineData("linux")]
    [InlineData("windows")]
    [InlineData("freebsd")]
    public void Image_Os_AcceptsVariousValues(string os)
    {
      // Arrange & Act
      var image = new Image { Os = os };

      // Assert
      Assert.Equal(os, image.Os);
    }

    [Theory]
    [InlineData("amd64")]
    [InlineData("arm64")]
    [InlineData("arm/v7")]
    [InlineData("386")]
    [InlineData("s390x")]
    public void Image_Architecture_AcceptsVariousValues(string arch)
    {
      // Arrange & Act
      var image = new Image { Architecture = arch };

      // Assert
      Assert.Equal(arch, image.Architecture);
    }

    [Fact]
    public void Image_IsNotSealed_CanBeSubclassed()
    {
      // Assert -- Image is declared as `class` not `sealed class`
      Assert.False(typeof(Image).IsSealed);
    }

    [Fact]
    public void Image_MultipleDigests_AllAccessible()
    {
      // Arrange & Act
      var image = new Image
      {
        Digests = new List<string>
        {
          "sha256:aaaa",
          "sha256:bbbb",
          "sha256:cccc"
        }
      };

      // Assert
      Assert.Equal(3, image.Digests.Count);
      Assert.Equal("sha256:aaaa", image.Digests[0]);
      Assert.Equal("sha256:bbbb", image.Digests[1]);
      Assert.Equal("sha256:cccc", image.Digests[2]);
    }

    #endregion

    #region Cross-model relationships

    [Fact]
    public void DockerImageRowResponse_TagsAndImageTags_AreIndependent()
    {
      // Arrange -- DockerImageRowResponse uses string[], Image uses List<string>
      var rowResponse = new DockerImageRowResponse
      {
        Id = "sha256:abc",
        Name = "nginx",
        Tags = new[] { "latest" }
      };

      var image = new Image
      {
        Id = "sha256:abc",
        Repository = "nginx",
        Tags = new List<string> { "latest" }
      };

      // Assert -- same data can be represented in both models
      Assert.Equal(rowResponse.Id, image.Id);
      Assert.Equal(rowResponse.Tags[0], image.Tags[0]);
    }

    #endregion
  }
}
