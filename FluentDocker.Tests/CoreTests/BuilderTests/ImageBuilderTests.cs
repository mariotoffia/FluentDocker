using System;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Kernel;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  // Tests build a real FluentDockerKernel via CreateMockKernel(), which
  // resolves the docker CLI binary at runtime. Reclassified as Integration
  // so the unit-test matrix on macOS/Windows runners (no Docker installed)
  // does not fail; truly mocking the kernel would require a kernel-stub
  // infrastructure that does not yet exist.
  [Trait("Category", "Integration")]
  public class ImageBuilderTests
  {
    [Fact]
    public void AsImageName_ReturnsBuilderForChaining()
    {
      var kernel = CreateMockKernel();
      var builder = new ImageBuilder(kernel, "docker");

      var result = builder.AsImageName("myapp:1.0");

      Assert.Same(builder, result);
    }

    [Fact]
    public void AsImageName_WithTag_ReturnsDockerfileBuilderForChaining()
    {
      var kernel = CreateMockKernel();
      var builder = new ImageBuilder(kernel, "docker");

      builder.AsImageName("myapp:v2.0");
      var dockerfileBuilder = builder.From("alpine");

      Assert.NotNull(dockerfileBuilder);
    }

    [Fact]
    public void ImageTag_ReturnsBuilderForChaining()
    {
      var kernel = CreateMockKernel();
      var builder = new ImageBuilder(kernel, "docker");

      var result = builder
          .AsImageName("myapp")
          .ImageTag("latest", "v1.0", "stable");

      Assert.Same(builder, result);
    }

    [Fact]
    public void BuildArguments_ReturnsBuilderForChaining()
    {
      var kernel = CreateMockKernel();
      var builder = new ImageBuilder(kernel, "docker");

      var result = builder.BuildArguments("VERSION=1.0", "DEBUG=true");

      Assert.Same(builder, result);
    }

    [Fact]
    public void Label_ReturnsBuilderForChaining()
    {
      var kernel = CreateMockKernel();
      var builder = new ImageBuilder(kernel, "docker");

      var result = builder.Label("maintainer=test@example.com", "version=1.0.0");

      Assert.Same(builder, result);
    }

    [Fact]
    public void NoCache_ReturnsBuilderForChaining()
    {
      var kernel = CreateMockKernel();
      var builder = new ImageBuilder(kernel, "docker");

      var result = builder.NoCache();

      Assert.Same(builder, result);
    }

    [Fact]
    public void AlwaysPull_ReturnsBuilderForChaining()
    {
      var kernel = CreateMockKernel();
      var builder = new ImageBuilder(kernel, "docker");

      var result = builder.AlwaysPull();

      Assert.Same(builder, result);
    }

    [Fact]
    public void RemoveIntermediate_ReturnsBuilderForChaining()
    {
      var kernel = CreateMockKernel();
      var builder = new ImageBuilder(kernel, "docker");

      var result = builder.RemoveIntermediate();

      Assert.Same(builder, result);
    }

    [Fact]
    public void RemoveIntermediate_WithForce_ReturnsBuilderForChaining()
    {
      var kernel = CreateMockKernel();
      var builder = new ImageBuilder(kernel, "docker");

      var result = builder.RemoveIntermediate(force: true);

      Assert.Same(builder, result);
    }

    [Fact]
    public void Platform_ReturnsBuilderForChaining()
    {
      var kernel = CreateMockKernel();
      var builder = new ImageBuilder(kernel, "docker");

      var result = builder.Platform("linux/amd64");

      Assert.Same(builder, result);
    }

    [Fact]
    public void Target_ReturnsBuilderForChaining()
    {
      var kernel = CreateMockKernel();
      var builder = new ImageBuilder(kernel, "docker");

      var result = builder.Target("builder");

      Assert.Same(builder, result);
    }

    [Fact]
    public void ReuseIfAlreadyExists_ReturnsBuilderForChaining()
    {
      var kernel = CreateMockKernel();
      var builder = new ImageBuilder(kernel, "docker");

      var result = builder.ReuseIfAlreadyExists();

      Assert.Same(builder, result);
    }

    [Fact]
    public void From_ReturnsDockerfileBuilder()
    {
      var kernel = CreateMockKernel();
      var builder = new ImageBuilder(kernel, "docker");

      var dockerfileBuilder = builder.From("alpine:latest");

      Assert.NotNull(dockerfileBuilder);
      Assert.IsType<DockerfileBuilder>(dockerfileBuilder);
    }

    [Fact]
    public void From_WithAsName_ReturnsDockerfileBuilder()
    {
      var kernel = CreateMockKernel();
      var builder = new ImageBuilder(kernel, "docker");

      var dockerfileBuilder = builder.From("node:18", "builder");

      Assert.NotNull(dockerfileBuilder);
    }

    [Fact]
    public void FromString_ReturnsDockerfileBuilder()
    {
      var kernel = CreateMockKernel();
      var builder = new ImageBuilder(kernel, "docker");

      var dockerfileBuilder = builder.FromString("FROM alpine\nRUN echo hello");

      Assert.NotNull(dockerfileBuilder);
    }

    [Fact]
    public void FromFile_ReturnsDockerfileBuilder()
    {
      var kernel = CreateMockKernel();
      var builder = new ImageBuilder(kernel, "docker");

      var dockerfileBuilder = builder.FromFile("/path/to/Dockerfile");

      Assert.NotNull(dockerfileBuilder);
    }

    [Fact]
    public void FluentChain_ReturnsBuilderForChaining()
    {
      var kernel = CreateMockKernel();
      var builder = new ImageBuilder(kernel, "docker");

      var result = builder
          .AsImageName("myapp")
          .ImageTag("latest", "v1.0")
          .BuildArguments("VERSION=1.0")
          .Label("maintainer=test@example.com")
          .NoCache()
          .AlwaysPull()
          .RemoveIntermediate(force: true)
          .Platform("linux/amd64")
          .Target("production")
          .ReuseIfAlreadyExists();

      Assert.Same(builder, result);
    }

    [Fact]
    public void Constructor_WithImageName_SetsName()
    {
      var kernel = CreateMockKernel();
      var builder = new ImageBuilder(kernel, "docker", "myapp:v1.0");

      // Should be able to continue with From
      var dockerfileBuilder = builder.From("alpine");
      Assert.NotNull(dockerfileBuilder);
    }

    [Fact]
    public void Constructor_RequiresKernel()
    {
      Assert.Throws<ArgumentNullException>(() => new ImageBuilder(null!, "docker"));
    }

    [Fact]
    public void Constructor_RequiresDriverId()
    {
      var kernel = CreateMockKernel();
      Assert.Throws<ArgumentNullException>(() => new ImageBuilder(kernel, null!));
    }

    private static FluentDockerKernel CreateMockKernel()
    {
      // Create a minimal kernel for testing
      return FluentDockerKernel.Create(NullLoggerFactory.Instance)
          .WithDockerCli("docker", d => d.AsDefault())
          .Build();
    }
  }
}
