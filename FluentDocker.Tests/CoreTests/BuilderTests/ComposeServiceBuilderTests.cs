using System;
using System.Reflection;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Model.Compose;
using FluentDocker.Model.Containers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Unit tests for <see cref="ComposeServiceBuilder"/>.
  /// The constructor is internal (strong-named assembly, no InternalsVisibleTo),
  /// so instances are created via reflection.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class ComposeServiceBuilderTests
  {
    /// <summary>
    /// Creates a ComposeServiceBuilder via reflection since the constructor is internal.
    /// </summary>
    private static ComposeServiceBuilder CreateBuilder(string name = "test-service")
    {
      var ctor = typeof(ComposeServiceBuilder).GetConstructor(
        BindingFlags.NonPublic | BindingFlags.Instance,
        binder: null,
        types: [typeof(string)],
        modifiers: null);

      Assert.NotNull(ctor);
      return (ComposeServiceBuilder)ctor.Invoke([name]);
    }

    /// <summary>
    /// Reads the private _config field from a ComposeServiceBuilder via reflection.
    /// </summary>
    private static ComposeServiceDefinition GetConfig(ComposeServiceBuilder builder)
    {
      var field = typeof(ComposeServiceBuilder).GetField(
        "_config",
        BindingFlags.NonPublic | BindingFlags.Instance);

      Assert.NotNull(field);
      return (ComposeServiceDefinition)field.GetValue(builder);
    }

    #region Construction

    [Fact]
    public void Constructor_SetsServiceName()
    {
      // Arrange & Act
      var builder = CreateBuilder("my-web-app");
      var config = GetConfig(builder);

      // Assert
      Assert.Equal("my-web-app", config.Name);
    }

    [Theory]
    [InlineData("redis")]
    [InlineData("postgres")]
    [InlineData("my_custom_service")]
    public void Constructor_SetsServiceName_Various(string name)
    {
      // Arrange & Act
      var builder = CreateBuilder(name);
      var config = GetConfig(builder);

      // Assert
      Assert.Equal(name, config.Name);
    }

    #endregion

    #region Image

    [Fact]
    public void Image_SetsImageName()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      var result = builder.Image("nginx:latest");

      // Assert
      var config = GetConfig(builder);
      Assert.Equal("nginx:latest", config.Image);
      Assert.Same(builder, result);
    }

    [Theory]
    [InlineData("redis")]
    [InlineData("ubuntu:22.04")]
    [InlineData("registry.example.com:5000/myapp:v2.1")]
    [InlineData("a4bc65fd")]
    public void Image_SetsVariousImageFormats(string image)
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      builder.Image(image);

      // Assert
      Assert.Equal(image, GetConfig(builder).Image);
    }

    [Fact]
    public void Image_OverwritesPreviousValue()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      builder.Image("first:v1").Image("second:v2");

      // Assert
      Assert.Equal("second:v2", GetConfig(builder).Image);
    }

    #endregion

    #region Restart

    [Theory]
    [InlineData(RestartPolicy.No)]
    [InlineData(RestartPolicy.Always)]
    [InlineData(RestartPolicy.OnFailure)]
    [InlineData(RestartPolicy.UnlessStopped)]
    public void Restart_SetsPolicy(RestartPolicy policy)
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      var result = builder.Restart(policy);

      // Assert
      Assert.Equal(policy, GetConfig(builder).Restart);
      Assert.Same(builder, result);
    }

    [Fact]
    public void Restart_OverwritesPreviousValue()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      builder.Restart(RestartPolicy.Always).Restart(RestartPolicy.OnFailure);

      // Assert
      Assert.Equal(RestartPolicy.OnFailure, GetConfig(builder).Restart);
    }

    #endregion

    #region DependsOn

    [Fact]
    public void DependsOn_AddsSingleService()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      var result = builder.DependsOn("redis");

      // Assert
      var deps = GetConfig(builder).DependsOn;
      Assert.Single(deps);
      Assert.Equal("redis", deps[0]);
      Assert.Same(builder, result);
    }

    [Fact]
    public void DependsOn_AddsMultipleServices()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      builder.DependsOn("redis", "postgres", "rabbitmq");

      // Assert
      var deps = GetConfig(builder).DependsOn;
      Assert.Equal(3, deps.Count);
      Assert.Contains("redis", deps);
      Assert.Contains("postgres", deps);
      Assert.Contains("rabbitmq", deps);
    }

    [Fact]
    public void DependsOn_NullArray_DoesNothing()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      var result = builder.DependsOn(services: null);

      // Assert
      Assert.Empty(GetConfig(builder).DependsOn);
      Assert.Same(builder, result);
    }

    [Fact]
    public void DependsOn_EmptyArray_DoesNothing()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      var result = builder.DependsOn([]);

      // Assert
      Assert.Empty(GetConfig(builder).DependsOn);
      Assert.Same(builder, result);
    }

    [Fact]
    public void DependsOn_MultipleCallsAccumulate()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      builder.DependsOn("redis").DependsOn("postgres");

      // Assert
      var deps = GetConfig(builder).DependsOn;
      Assert.Equal(2, deps.Count);
      Assert.Equal("redis", deps[0]);
      Assert.Equal("postgres", deps[1]);
    }

    #endregion
  }
}
