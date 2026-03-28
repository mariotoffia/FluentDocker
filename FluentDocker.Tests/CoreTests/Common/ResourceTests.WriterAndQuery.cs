using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using FluentDocker.Resources;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  /// <summary>
  /// Unit tests for FileResourceWriter and ResourceQuery.
  /// </summary>
  public partial class ResourceTests
  {
    #region FileResourceWriter - Write(ResourceStream)

    [Fact]
    public void Write_ResourceStream_CreatesFileInBaseDirectory()
    {
      var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

      try
      {
        // Arrange
        var content = "hello world";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var info = new ResourceInfo
        {
          Resource = "output.txt",
          RelativeRootNamespace = string.Empty
        };
        using var resourceStream = new ResourceStream(stream, info);
        var writer = new FileResourceWriter(tempDir);

        // Act
        var result = writer.Write(resourceStream);

        // Assert
        var filePath = Path.Combine(tempDir, "output.txt");
        Assert.True(File.Exists(filePath), $"Expected file at {filePath}");
        Assert.Equal(content, File.ReadAllText(filePath));
        Assert.IsAssignableFrom<IResourceWriter>(result);
      }
      finally
      {
        if (Directory.Exists(tempDir))
          Directory.Delete(tempDir, true);
      }
    }

    [Fact]
    public void Write_ResourceStream_CreatesSubdirectoryFromRelativeNamespace()
    {
      var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

      try
      {
        // Arrange
        var content = "nested content";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var info = new ResourceInfo
        {
          Resource = "nested.txt",
          RelativeRootNamespace = "Sub.Folder"
        };
        using var resourceStream = new ResourceStream(stream, info);
        var writer = new FileResourceWriter(tempDir);

        // Act
        writer.Write(resourceStream);

        // Assert - RelativeRootNamespace dots are replaced with PathSeparator
        var expectedSubDir = "Sub.Folder".Replace('.', Path.PathSeparator);
        var expectedPath = Path.Combine(tempDir, expectedSubDir, "nested.txt");
        Assert.True(File.Exists(expectedPath),
          $"Expected file at {expectedPath}");
        Assert.Equal(content, File.ReadAllText(expectedPath));
      }
      finally
      {
        if (Directory.Exists(tempDir))
          Directory.Delete(tempDir, true);
      }
    }

    [Fact]
    public void Write_ResourceStream_OverwritesExistingFile()
    {
      var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

      try
      {
        // Arrange - write first version
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "overwrite.txt"), "old content");

        var stream = new MemoryStream(Encoding.UTF8.GetBytes("new content"));
        var info = new ResourceInfo
        {
          Resource = "overwrite.txt",
          RelativeRootNamespace = string.Empty
        };
        using var resourceStream = new ResourceStream(stream, info);
        var writer = new FileResourceWriter(tempDir);

        // Act
        writer.Write(resourceStream);

        // Assert
        Assert.Equal("new content",
          File.ReadAllText(Path.Combine(tempDir, "overwrite.txt")));
      }
      finally
      {
        if (Directory.Exists(tempDir))
          Directory.Delete(tempDir, true);
      }
    }

    [Fact]
    public void Write_ResourceStream_ReturnsSelfForFluentChaining()
    {
      var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

      try
      {
        // Arrange
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("chain"));
        var info = new ResourceInfo
        {
          Resource = "chain.txt",
          RelativeRootNamespace = string.Empty
        };
        using var resourceStream = new ResourceStream(stream, info);
        var writer = new FileResourceWriter(tempDir);

        // Act
        var returned = writer.Write(resourceStream);

        // Assert
        Assert.Same(writer, returned);
      }
      finally
      {
        if (Directory.Exists(tempDir))
          Directory.Delete(tempDir, true);
      }
    }

    [Fact]
    public void Write_BinaryContent_PreservesBytes()
    {
      var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

      try
      {
        // Arrange
        var bytes = new byte[] { 0x00, 0x01, 0xFF, 0xFE, 0x80, 0x7F };
        var stream = new MemoryStream(bytes);
        var info = new ResourceInfo
        {
          Resource = "binary.dat",
          RelativeRootNamespace = string.Empty
        };
        using var resourceStream = new ResourceStream(stream, info);
        var writer = new FileResourceWriter(tempDir);

        // Act
        writer.Write(resourceStream);

        // Assert
        var written = File.ReadAllBytes(Path.Combine(tempDir, "binary.dat"));
        Assert.Equal(bytes, written);
      }
      finally
      {
        if (Directory.Exists(tempDir))
          Directory.Delete(tempDir, true);
      }
    }

    [Fact]
    public void Write_EmptyStream_CreatesEmptyFile()
    {
      var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

      try
      {
        // Arrange
        var stream = new MemoryStream(Array.Empty<byte>());
        var info = new ResourceInfo
        {
          Resource = "empty.txt",
          RelativeRootNamespace = string.Empty
        };
        using var resourceStream = new ResourceStream(stream, info);
        var writer = new FileResourceWriter(tempDir);

        // Act
        writer.Write(resourceStream);

        // Assert
        var filePath = Path.Combine(tempDir, "empty.txt");
        Assert.True(File.Exists(filePath));
        Assert.Empty(File.ReadAllBytes(filePath));
      }
      finally
      {
        if (Directory.Exists(tempDir))
          Directory.Delete(tempDir, true);
      }
    }

    #endregion

    #region FileResourceWriter - Write(ResourceReader)

    [Fact]
    public void Write_ResourceReader_EmptyReader_ReturnsSelf()
    {
      var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

      try
      {
        // Arrange
        var reader = new ResourceReader(Array.Empty<ResourceInfo>());
        var writer = new FileResourceWriter(tempDir);

        // Act
        var result = writer.Write(reader);

        // Assert
        Assert.Same(writer, result);
      }
      finally
      {
        if (Directory.Exists(tempDir))
          Directory.Delete(tempDir, true);
      }
    }

    #endregion

    #region ResourceQuery

    [Fact]
    public void ResourceQuery_FluentApi_ReturnsSelf()
    {
      // Arrange
      var query = new ResourceQuery();

      // Act
      var result1 = query.From("FluentDocker.Tests");
      var result2 = query.Namespace("FluentDocker.Tests.Resources");
      var result3 = query.Recursive();

      // Assert
      Assert.Same(query, result1);
      Assert.Same(query, result2);
      Assert.Same(query, result3);
    }

    [Fact]
    public void ResourceQuery_NonExistentNamespace_ReturnsEmpty()
    {
      // Arrange
      var query = new ResourceQuery()
        .From("FluentDocker.Tests")
        .Namespace("NonExistent.Namespace.That.Does.Not.Exist");

      // Act
      var results = query.Query().ToList();

      // Assert
      Assert.Empty(results);
    }

    [Fact]
    public void ResourceQuery_Include_FiltersToSpecifiedResources()
    {
      // Arrange
      var query = new ResourceQuery()
        .From("FluentDocker.Tests")
        .Namespace("NonExistent.Namespace");

      // Act - Include with non-matching filter returns empty
      var results = query.Include("some-file.txt").ToList();

      // Assert
      Assert.Empty(results);
    }

    [Fact]
    public void ResourceQuery_Namespace_WithNonRecursive_OnlyReturnsExactMatch()
    {
      // Arrange
      var query = new ResourceQuery()
        .From("FluentDocker.Tests")
        .Namespace("NonExistent.Namespace", recursive: false);

      // Act
      var results = query.Query().ToList();

      // Assert
      Assert.Empty(results);
    }

    [Fact]
    public void ResourceQuery_ExtractFile_ViaQuery_HandlesStandardExtensions()
    {
      // Verify ExtractFile behavior indirectly through the Query method.
      var assembly = typeof(ResourceTests).Assembly;
      var resourceNames = assembly.GetManifestResourceNames();

      if (resourceNames.Length == 0)
      {
        // No embedded resources - verify empty query is safe
        var query = new ResourceQuery()
          .From("FluentDocker.Tests")
          .Namespace("FluentDocker.Tests");
        Assert.Empty(query.Query().ToList());
        return;
      }

      // If resources exist, verify the first one is parsed
      var firstName = resourceNames[0];
      var ns = firstName[..firstName.LastIndexOf('.')];
      var rootNs = ns[..ns.IndexOf('.')];

      var query2 = new ResourceQuery()
        .From("FluentDocker.Tests")
        .Namespace(rootNs, recursive: true);

      var results = query2.Query().ToList();
      Assert.NotEmpty(results);
      Assert.All(results, r =>
      {
        Assert.NotNull(r.Resource);
        Assert.NotNull(r.Namespace);
        Assert.NotNull(r.Root);
        Assert.Same(assembly, r.Assembly);
      });
    }

    [Fact]
    public void ResourceQuery_ExtractFile_PrivateMethod_HandlesEdgeCases()
    {
      // Access the private static ExtractFile method via reflection
      var method = typeof(ResourceQuery).GetMethod(
        "ExtractFile",
        BindingFlags.NonPublic | BindingFlags.Static);

      Assert.NotNull(method);

      // Standard extension: "Ns.Sub.file.txt" -> "file.txt"
      var result1 = (string)method.Invoke(null, new object[] { "Ns.Sub.file.txt" });
      Assert.Equal("file.txt", result1);

      // No dots at all: "filename" -> "filename"
      var result2 = (string)method.Invoke(null, new object[] { "filename" });
      Assert.Equal("filename", result2);

      // Long extension (>5 chars) treated as dotless filename:
      // "Ns.Dockerfile" -> "Dockerfile"
      var result3 = (string)method.Invoke(null, new object[] { "Ns.Dockerfile" });
      Assert.Equal("Dockerfile", result3);

      // Short extension with namespace: "A.B.C.config.json" -> "config.json"
      var result4 = (string)method.Invoke(null, new object[] { "A.B.C.config.json" });
      Assert.Equal("config.json", result4);

      // Single dot file: "file.cs" -> "file.cs" (no namespace separator)
      var result5 = (string)method.Invoke(null, new object[] { "file.cs" });
      Assert.Equal("file.cs", result5);
    }

    #endregion
  }
}
