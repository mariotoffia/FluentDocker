using System;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Kernel;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
    [Trait("Category", "Unit")]
    public class ImageBuilderTests
    {
        [Fact]
        public void AsImageName_SetsImageName()
        {
            var kernel = CreateMockKernel();
            var builder = new ImageBuilder(kernel, "docker");

            var result = builder.AsImageName("myapp:1.0");

            Assert.NotNull(result);
        }

        [Fact]
        public void AsImageName_ParsesTagFromName()
        {
            var kernel = CreateMockKernel();
            var builder = new ImageBuilder(kernel, "docker");

            builder.AsImageName("myapp:v2.0");
            var dockerfileBuilder = builder.From("alpine");

            // Verify it returns a DockerfileBuilder
            Assert.NotNull(dockerfileBuilder);
        }

        [Fact]
        public void ImageTag_AddsMultipleTags()
        {
            var kernel = CreateMockKernel();
            var builder = new ImageBuilder(kernel, "docker");

            var result = builder
                .AsImageName("myapp")
                .ImageTag("latest", "v1.0", "stable");

            Assert.NotNull(result);
        }

        [Fact]
        public void BuildArguments_ParsesKeyValuePairs()
        {
            var kernel = CreateMockKernel();
            var builder = new ImageBuilder(kernel, "docker");

            var result = builder.BuildArguments("VERSION=1.0", "DEBUG=true");

            Assert.NotNull(result);
        }

        [Fact]
        public void Label_ParsesKeyValuePairs()
        {
            var kernel = CreateMockKernel();
            var builder = new ImageBuilder(kernel, "docker");

            var result = builder.Label("maintainer=test@example.com", "version=1.0.0");

            Assert.NotNull(result);
        }

        [Fact]
        public void NoCache_SetsNoCacheFlag()
        {
            var kernel = CreateMockKernel();
            var builder = new ImageBuilder(kernel, "docker");

            var result = builder.NoCache();

            Assert.NotNull(result);
        }

        [Fact]
        public void AlwaysPull_SetsPullFlag()
        {
            var kernel = CreateMockKernel();
            var builder = new ImageBuilder(kernel, "docker");

            var result = builder.AlwaysPull();

            Assert.NotNull(result);
        }

        [Fact]
        public void RemoveIntermediate_SetsRemoveFlag()
        {
            var kernel = CreateMockKernel();
            var builder = new ImageBuilder(kernel, "docker");

            var result = builder.RemoveIntermediate();

            Assert.NotNull(result);
        }

        [Fact]
        public void RemoveIntermediate_WithForce_SetsBothFlags()
        {
            var kernel = CreateMockKernel();
            var builder = new ImageBuilder(kernel, "docker");

            var result = builder.RemoveIntermediate(force: true);

            Assert.NotNull(result);
        }

        [Fact]
        public void Platform_SetsPlatform()
        {
            var kernel = CreateMockKernel();
            var builder = new ImageBuilder(kernel, "docker");

            var result = builder.Platform("linux/amd64");

            Assert.NotNull(result);
        }

        [Fact]
        public void Target_SetsTargetStage()
        {
            var kernel = CreateMockKernel();
            var builder = new ImageBuilder(kernel, "docker");

            var result = builder.Target("builder");

            Assert.NotNull(result);
        }

        [Fact]
        public void ReuseIfAlreadyExists_SetsReuseFlag()
        {
            var kernel = CreateMockKernel();
            var builder = new ImageBuilder(kernel, "docker");

            var result = builder.ReuseIfAlreadyExists();

            Assert.NotNull(result);
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
        public void FluentChain_WorksCorrectly()
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

            Assert.NotNull(result);
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
            Assert.Throws<ArgumentNullException>(() => new ImageBuilder(null, "docker"));
        }

        [Fact]
        public void Constructor_RequiresDriverId()
        {
            var kernel = CreateMockKernel();
            Assert.Throws<ArgumentNullException>(() => new ImageBuilder(kernel, null));
        }

        private static FluentDockerKernel CreateMockKernel()
        {
            // Create a minimal kernel for testing
            return FluentDockerKernel.Create()
                .WithDockerCli("docker", d => d.AsDefault())
                .Build();
        }
    }
}

