#pragma warning disable CS0618 // Obsolete types under test

using System;
using System.Collections.Generic;
using System.Linq;
using FluentDocker.Common;
using FluentDocker.Model;
using FluentDocker.Services;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  /// <summary>
  /// Unit tests for <see cref="FeatureAttribute"/>, <see cref="IFeature"/>,
  /// and <see cref="FeatureConstants"/>.
  /// </summary>
  [Trait("Category", "Unit")]
  public class FeatureAttributeTests
  {
    [Fact]
    public void DefaultConstructor_SetsEmptyDependencies()
    {
      // Arrange & Act
      var attr = new FeatureAttribute();

      // Assert
      Assert.Null(attr.Id);
      Assert.NotNull(attr.Dependencies);
      Assert.Empty(attr.Dependencies);
    }

    [Fact]
    public void ParameterizedConstructor_SetsIdAndDependencies()
    {
      // Arrange & Act
      var attr = new FeatureAttribute("my-feature");

      // Assert
      Assert.Equal("my-feature", attr.Id);
      Assert.Null(attr.Dependencies);
    }

    [Fact]
    public void Validate_NullId_ThrowsFluentDockerException()
    {
      // Arrange
      var attr = new FeatureAttribute { Id = null };

      // Act & Assert
      var ex = Assert.Throws<FluentDockerException>(() => attr.Validate());
      Assert.Contains("valid Id", ex.Message);
    }

    [Fact]
    public void Validate_ValidId_NoDependencies_DoesNotThrow()
    {
      // Arrange
      var attr = new FeatureAttribute { Id = "x", Dependencies = Array.Empty<Type>() };

      // Act & Assert (should not throw)
      attr.Validate();
    }

    [Fact]
    public void Validate_NonIFeatureDependency_ThrowsFluentDockerException()
    {
      // Arrange
      var attr = new FeatureAttribute("bad-feature", new[] { typeof(string) });

      // Act & Assert
      var ex = Assert.Throws<FluentDockerException>(() => attr.Validate());
      Assert.Contains("non IFeature type", ex.Message);
      Assert.Contains("bad-feature", ex.Message);
    }

    [Fact]
    public void Validate_IFeatureDependency_DoesNotThrow()
    {
      // Arrange
      var attr = new FeatureAttribute("good-feature", new[] { typeof(DummyFeature) });

      // Act & Assert (should not throw)
      attr.Validate();
    }

    [Fact]
    public void FeatureConstants_KeepOnDispose_HasExpectedValue()
    {
      Assert.Equal("global.keep.on.dispose", FeatureConstants.KeepOnDispose);
    }

    [Fact]
    public void FeatureConstants_HostService_HasExpectedValue()
    {
      // Note: the original constant contains a typo ("globa" instead of "global").
      Assert.Equal("globa.host.service", FeatureConstants.HostService);
    }

    /// <summary>
    /// Minimal <see cref="IFeature"/> implementation used as a valid dependency type.
    /// </summary>
    private sealed class DummyFeature : IFeature
    {
      public string Id => throw new NotImplementedException();
      public IEnumerable<IServiceAsync> Services => throw new NotImplementedException();
      public void Initialize(IDictionary<string, object> settings = null!) =>
        throw new NotImplementedException();
      public void Execute(params string[] arguments) =>
        throw new NotImplementedException();
      public void Dispose() { }
    }
  }
}
