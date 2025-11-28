using System;
using System.Collections.Generic;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
    /// <summary>
    /// Unit tests for ComposeService.
    /// </summary>
    [Trait("Category", "Unit")]
    public class ComposeServiceTests
    {
        [Fact]
        public void Constructor_SetsProperties()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var composeFiles = new List<string> { "docker-compose.yml" };

            // Act
            var service = new ComposeService(kernel, "docker", composeFiles, "my-project");

            // Assert
            Assert.Equal("my-project", service.Name);
            Assert.Equal("my-project", service.ProjectName);
            Assert.Single(service.ComposeFiles);
            Assert.Equal("docker-compose.yml", service.ComposeFiles[0]);
            Assert.Equal(kernel, service.Kernel);
            Assert.Equal("docker", service.DriverId);
            Assert.Equal(ServiceRunningState.Running, service.State);

            kernel.Dispose();
        }

        [Fact]
        public void Constructor_NullKernel_ThrowsArgumentNullException()
        {
            var composeFiles = new List<string> { "docker-compose.yml" };
            Assert.Throws<ArgumentNullException>(() =>
                new ComposeService(null!, "docker", composeFiles, "my-project"));
        }

        [Fact]
        public void Constructor_NullDriverId_ThrowsArgumentNullException()
        {
            var kernel = new FluentDockerKernel();
            var composeFiles = new List<string> { "docker-compose.yml" };
            Assert.Throws<ArgumentNullException>(() =>
                new ComposeService(kernel, null!, composeFiles, "my-project"));
            kernel.Dispose();
        }

        [Fact]
        public void Constructor_NullComposeFiles_ThrowsArgumentNullException()
        {
            var kernel = new FluentDockerKernel();
            Assert.Throws<ArgumentNullException>(() =>
                new ComposeService(kernel, "docker", null!, "my-project"));
            kernel.Dispose();
        }

        [Fact]
        public void Constructor_NullProjectName_ThrowsArgumentNullException()
        {
            var kernel = new FluentDockerKernel();
            var composeFiles = new List<string> { "docker-compose.yml" };
            Assert.Throws<ArgumentNullException>(() =>
                new ComposeService(kernel, "docker", composeFiles, null!));
            kernel.Dispose();
        }

        [Fact]
        public void Constructor_WithRemoveVolumes_SetsFlag()
        {
            var kernel = new FluentDockerKernel();
            var composeFiles = new List<string> { "docker-compose.yml" };
            var service = new ComposeService(kernel, "docker", composeFiles, "my-project",
                removeVolumes: true);

            Assert.NotNull(service);
            kernel.Dispose();
        }

        [Fact]
        public void Constructor_WithRemoveImages_SetsFlag()
        {
            var kernel = new FluentDockerKernel();
            var composeFiles = new List<string> { "docker-compose.yml" };
            var service = new ComposeService(kernel, "docker", composeFiles, "my-project",
                removeImages: true);

            Assert.NotNull(service);
            kernel.Dispose();
        }

        [Fact]
        public void Constructor_MultipleComposeFiles_AllStored()
        {
            var kernel = new FluentDockerKernel();
            var composeFiles = new List<string>
            {
                "docker-compose.yml",
                "docker-compose.override.yml",
                "docker-compose.prod.yml"
            };

            var service = new ComposeService(kernel, "docker", composeFiles, "my-project");

            Assert.Equal(3, service.ComposeFiles.Count);
            kernel.Dispose();
        }

        [Fact]
        public void PauseAsync_ThrowsNotSupportedException()
        {
            var kernel = new FluentDockerKernel();
            var composeFiles = new List<string> { "docker-compose.yml" };
            var service = new ComposeService(kernel, "docker", composeFiles, "my-project");

            Assert.ThrowsAsync<NotSupportedException>(async () => await service.PauseAsync());
            kernel.Dispose();
        }

        [Fact]
        public void AddHook_AddsHook()
        {
            var kernel = new FluentDockerKernel();
            var composeFiles = new List<string> { "docker-compose.yml" };
            var service = new ComposeService(kernel, "docker", composeFiles, "my-project");

            service.AddHook(ServiceRunningState.Removed, async _ => { }, "test-hook");
            Assert.NotNull(service);

            kernel.Dispose();
        }

        [Fact]
        public void RemoveHook_RemovesHook()
        {
            var kernel = new FluentDockerKernel();
            var composeFiles = new List<string> { "docker-compose.yml" };
            var service = new ComposeService(kernel, "docker", composeFiles, "my-project");
            service.AddHook(ServiceRunningState.Removed, async _ => { }, "test-hook");

            service.RemoveHook("test-hook");
            Assert.NotNull(service);

            kernel.Dispose();
        }
    }
}

