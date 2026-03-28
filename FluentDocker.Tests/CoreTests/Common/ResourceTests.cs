using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FluentDocker.Resources;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  /// <summary>
  /// Unit tests for ResourceInfo, ResourceStream, and ResourceReader.
  /// FileResourceWriter and ResourceQuery tests are in the partial class file.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class ResourceTests
  {
    #region ResourceInfo

    [Fact]
    public void ResourceInfo_Properties_AreSettableAndGettable()
    {
      // Arrange & Act
      var info = new ResourceInfo
      {
        Resource = "test.txt",
        Namespace = "FluentDocker.Tests.Resources",
        Root = "FluentDocker.Tests",
        RelativeRootNamespace = "Resources",
        Assembly = typeof(ResourceTests).Assembly
      };

      // Assert
      Assert.Equal("test.txt", info.Resource);
      Assert.Equal("FluentDocker.Tests.Resources", info.Namespace);
      Assert.Equal("FluentDocker.Tests", info.Root);
      Assert.Equal("Resources", info.RelativeRootNamespace);
      Assert.Same(typeof(ResourceTests).Assembly, info.Assembly);
    }

    [Fact]
    public void ResourceInfo_DefaultValues_AreNull()
    {
      // Arrange & Act
      var info = new ResourceInfo();

      // Assert
      Assert.Null(info.Resource);
      Assert.Null(info.Namespace);
      Assert.Null(info.Root);
      Assert.Null(info.RelativeRootNamespace);
      Assert.Null(info.Assembly);
    }

    #endregion

    #region ResourceStream

    [Fact]
    public void ResourceStream_Constructor_StoresStreamAndInfo()
    {
      // Arrange
      var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
      var info = new ResourceInfo
      {
        Resource = "hello.txt",
        Namespace = "Test.Ns",
        Root = "Test",
        RelativeRootNamespace = "Ns"
      };

      // Act
      using var resourceStream = new ResourceStream(stream, info);

      // Assert
      Assert.Same(stream, resourceStream.Stream);
      Assert.Same(info, resourceStream.Info);
    }

    [Fact]
    public void ResourceStream_Dispose_DisposesUnderlyingStream()
    {
      // Arrange
      var stream = new MemoryStream(Encoding.UTF8.GetBytes("data"));
      var info = new ResourceInfo { Resource = "data.bin" };
      var resourceStream = new ResourceStream(stream, info);

      // Act
      resourceStream.Dispose();

      // Assert - reading from disposed stream should throw
      Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
    }

    [Fact]
    public void ResourceStream_StreamContent_IsReadable()
    {
      // Arrange
      var content = "file content here";
      var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
      var info = new ResourceInfo { Resource = "content.txt" };

      // Act
      using var resourceStream = new ResourceStream(stream, info);
      using var reader = new StreamReader(resourceStream.Stream);
      var result = reader.ReadToEnd();

      // Assert
      Assert.Equal(content, result);
    }

    #endregion

    #region ResourceReader

    [Fact]
    public void ResourceReader_EmptyCollection_YieldsNoElements()
    {
      // Arrange
      var reader = new ResourceReader(Array.Empty<ResourceInfo>());

      // Act
      var items = reader.ToList();

      // Assert
      Assert.Empty(items);
    }

    [Fact]
    public void ResourceReader_Enumerator_Reset_AllowsReIteration()
    {
      // Arrange - use empty collection to test enumerator mechanics
      var reader = new ResourceReader(Array.Empty<ResourceInfo>());

      // Act
      using var enumerator = reader.GetEnumerator();
      var firstPass = enumerator.MoveNext();
      enumerator.Reset();
      var secondPass = enumerator.MoveNext();

      // Assert
      Assert.False(firstPass);
      Assert.False(secondPass);
    }

    [Fact]
    public void ResourceReader_NonGenericEnumerator_Works()
    {
      // Arrange
      var reader = new ResourceReader(Array.Empty<ResourceInfo>());
      var enumerable = (System.Collections.IEnumerable)reader;

      // Act
      var enumerator = enumerable.GetEnumerator();
      var hasItems = enumerator.MoveNext();

      // Assert
      Assert.False(hasItems);
    }

    [Fact]
    public void ResourceReader_WithRealAssemblyResources_IteratesResources()
    {
      // Arrange - query actual manifest resources from the test assembly
      var assembly = typeof(ResourceTests).Assembly;
      var resourceNames = assembly.GetManifestResourceNames();

      // If no embedded resources exist, this test verifies the empty path
      if (resourceNames.Length == 0)
      {
        var reader = new ResourceReader(Array.Empty<ResourceInfo>());
        Assert.Empty(reader.ToList());
        return;
      }

      // Build ResourceInfo for the first embedded resource found
      var firstResource = resourceNames[0];
      var lastDot = firstResource.LastIndexOf('.');
      var secondLastDot = firstResource.LastIndexOf('.', lastDot - 1);
      var ns = firstResource[..secondLastDot];
      var file = firstResource[(secondLastDot + 1)..];

      var infos = new[]
      {
        new ResourceInfo
        {
          Assembly = assembly,
          Namespace = ns,
          Root = ns,
          RelativeRootNamespace = string.Empty,
          Resource = file
        }
      };

      var reader2 = new ResourceReader(infos);

      // Act
      var streams = new List<ResourceStream>();
      try
      {
        foreach (var rs in reader2)
          streams.Add(rs);

        // Assert
        Assert.Single(streams);
        Assert.NotNull(streams[0].Stream);
        Assert.Equal(file, streams[0].Info.Resource);
      }
      finally
      {
        foreach (var s in streams)
          s.Dispose();
      }
    }

    #endregion
  }
}
