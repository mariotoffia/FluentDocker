using System;
using FluentDocker.Model.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  /// <summary>
  /// Unit tests for <see cref="EmbeddedUri"/>.
  /// </summary>
  [Trait("Category", "Unit")]
  public class EmbeddedUriTests
  {
    #region Constructor Parsing Tests

    [Fact]
    public void Constructor_ValidThreeSegmentUri_ParsesAllProperties()
    {
      // Arrange & Act
      var uri = new EmbeddedUri("emb:MyAssembly/My.Namespace/myfile.txt");

      // Assert
      Assert.Equal("MyAssembly", uri.Host);
      Assert.Equal("MyAssembly", uri.Assembly);
      Assert.Equal("My.Namespace", uri.Namespace);
      Assert.Equal("myfile.txt", uri.Resource);
    }

    [Fact]
    public void Constructor_TwoSegmentUri_ParsesAssemblyAndNamespace_ResourceIsNull()
    {
      // Arrange & Act
      var uri = new EmbeddedUri("emb:MyAssembly/My.Namespace");

      // Assert
      Assert.Equal("MyAssembly", uri.Host);
      Assert.Equal("MyAssembly", uri.Assembly);
      Assert.Equal("My.Namespace", uri.Namespace);
      Assert.Null(uri.Resource);
    }

    [Fact]
    public void Constructor_AssemblyProperty_EqualsHost()
    {
      // Arrange & Act
      var uri = new EmbeddedUri("emb:SomeLib/Some.NS/data.json");

      // Assert
      Assert.Equal(uri.Host, uri.Assembly);
    }

    [Fact]
    public void Constructor_ResourceWithDots_PreservedExactly()
    {
      // Arrange & Act
      var uri = new EmbeddedUri("emb:MyAssembly/My.Namespace/my.config.json");

      // Assert
      Assert.Equal("my.config.json", uri.Resource);
    }

    [Fact]
    public void Constructor_NamespaceWithMultipleDots_PreservedExactly()
    {
      // Arrange & Act
      var uri = new EmbeddedUri("emb:MyAssembly/My.Deep.Nested.Namespace/file.txt");

      // Assert
      Assert.Equal("My.Deep.Nested.Namespace", uri.Namespace);
    }

    [Fact]
    public void Constructor_SimpleNames_ParsesCorrectly()
    {
      // Arrange & Act
      var uri = new EmbeddedUri("emb:Lib/NS/Res");

      // Assert
      Assert.Equal("Lib", uri.Assembly);
      Assert.Equal("NS", uri.Namespace);
      Assert.Equal("Res", uri.Resource);
    }

    #endregion

    #region Scheme Validation Tests

    [Fact]
    public void Constructor_InvalidScheme_Throws()
    {
      // Uri base ctor may throw UriFormatException before our ArgumentException,
      // depending on how the runtime handles the scheme. Either exception is acceptable.
      Assert.ThrowsAny<Exception>(
        () => new EmbeddedUri("http:MyAssembly/My.Namespace/file.txt"));
    }

    [Fact]
    public void Constructor_WrongScheme_Throws()
    {
      // Same as above: base Uri ctor may reject the format before our scheme check runs.
      Assert.ThrowsAny<Exception>(
        () => new EmbeddedUri("file:MyAssembly/My.Namespace/file.txt"));
    }

    [Fact]
    public void Constructor_CaseInsensitiveScheme_Accepted()
    {
      // Arrange & Act - the prefix check uses OrdinalIgnoreCase
      var uri = new EmbeddedUri("EMB:MyAssembly/My.Namespace/file.txt");

      // Assert
      Assert.Equal("MyAssembly", uri.Assembly);
      Assert.Equal("My.Namespace", uri.Namespace);
      Assert.Equal("file.txt", uri.Resource);
    }

    [Fact]
    public void Constructor_MixedCaseScheme_Accepted()
    {
      // Arrange & Act
      var uri = new EmbeddedUri("Emb:MyAssembly/My.Namespace/resource.xml");

      // Assert
      Assert.Equal("MyAssembly", uri.Assembly);
      Assert.Equal("My.Namespace", uri.Namespace);
      Assert.Equal("resource.xml", uri.Resource);
    }

    #endregion

    #region Implicit Conversion Tests

    [Fact]
    public void ImplicitConversion_NullString_ReturnsNull()
    {
      // Arrange & Act
      EmbeddedUri uri = (string)null;

      // Assert
      Assert.Null(uri);
    }

    [Fact]
    public void ImplicitConversion_ValidString_ReturnsEmbeddedUri()
    {
      // Arrange & Act
      EmbeddedUri uri = "emb:TestAssembly/Test.Namespace/test.txt";

      // Assert
      Assert.NotNull(uri);
      Assert.Equal("TestAssembly", uri.Assembly);
      Assert.Equal("Test.Namespace", uri.Namespace);
      Assert.Equal("test.txt", uri.Resource);
    }

    [Fact]
    public void ImplicitConversion_InvalidScheme_ThrowsArgumentException()
    {
      // Arrange & Act & Assert
      Assert.Throws<ArgumentException>(() =>
      {
        EmbeddedUri uri = "wrong:Assembly/Namespace/Resource";
      });
    }

    #endregion

    #region Inheritance Tests

    [Fact]
    public void EmbeddedUri_IsUri()
    {
      // Arrange & Act
      var uri = new EmbeddedUri("emb:MyAssembly/My.Namespace/file.txt");

      // Assert
      Assert.IsAssignableFrom<Uri>(uri);
    }

    [Fact]
    public void Scheme_IsEmb_ValidatedByConstructor()
    {
      // Arrange & Act - "emb" scheme is accepted
      var uri = new EmbeddedUri("emb:Asm/Ns/Res");

      // Assert - constructing with "emb" succeeds, proving that is the expected prefix
      Assert.NotNull(uri);
      Assert.Equal("Asm", uri.Assembly);
    }

    #endregion
  }
}
