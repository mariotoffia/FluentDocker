using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
    /// <summary>
    /// Unit tests for the typed fluent builder wrappers (DockerCliFluentBuilder,
    /// DockerApiFluentBuilder, PodmanCliFluentBuilder) and PodBuilder.
    /// </summary>
    [Trait("Category", "Unit")]
    public class TypedFluentBuilderTests
    {
        #region WithinDockerCli

        [Fact]
        public async Task WithinDockerCli_ReturnsDockerCliFluentBuilder()
        {
            var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            try
            {
                var result = new Builder().WithinDockerCli("docker", kernel);
                Assert.NotNull(result);
                Assert.IsType<DockerCliFluentBuilder>(result);
            }
            finally { kernel.Dispose(); }
        }

        [Fact]
        public async Task WithinDockerCli_UseContainer_ReturnsDockerCliBuilder()
        {
            var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            try
            {
                var result = new Builder()
                    .WithinDockerCli("docker", kernel)
                    .UseContainer(c => c.UseImage("alpine:latest").WithName("test"));

                Assert.NotNull(result);
                Assert.IsType<DockerCliFluentBuilder>(result);
            }
            finally { kernel.Dispose(); }
        }

        [Fact]
        public async Task WithinDockerCli_UseNetwork_ReturnsDockerCliBuilder()
        {
            var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            try
            {
                var result = new Builder()
                    .WithinDockerCli("docker", kernel)
                    .UseNetwork(n => n.WithName("test-net"));

                Assert.NotNull(result);
                Assert.IsType<DockerCliFluentBuilder>(result);
            }
            finally { kernel.Dispose(); }
        }

        [Fact]
        public async Task WithinDockerCli_UseVolume_ReturnsDockerCliBuilder()
        {
            var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            try
            {
                var result = new Builder()
                    .WithinDockerCli("docker", kernel)
                    .UseVolume(v => v.WithName("test-vol"));

                Assert.NotNull(result);
                Assert.IsType<DockerCliFluentBuilder>(result);
            }
            finally { kernel.Dispose(); }
        }

        [Fact]
        public async Task WithinDockerCli_UseCompose_ReturnsDockerCliBuilder()
        {
            var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            try
            {
                var result = new Builder()
                    .WithinDockerCli("docker", kernel)
                    .UseCompose(c => c.WithComposeFile("docker-compose.yml"));

                Assert.NotNull(result);
                Assert.IsType<DockerCliFluentBuilder>(result);
            }
            finally { kernel.Dispose(); }
        }

        [Fact]
        public async Task WithinDockerCli_Chaining_Works()
        {
            var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            try
            {
                var result = new Builder()
                    .WithinDockerCli("docker", kernel)
                    .UseNetwork(n => n.WithName("net"))
                    .UseVolume(v => v.WithName("vol"))
                    .UseContainer(c => c.UseImage("alpine").WithName("test"));

                Assert.NotNull(result);
                Assert.IsType<DockerCliFluentBuilder>(result);
            }
            finally { kernel.Dispose(); }
        }

        #endregion

        #region WithinDockerApi

        [Fact]
        public async Task WithinDockerApi_ReturnsDockerApiFluentBuilder()
        {
            var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("api");
            try
            {
                var result = new Builder().WithinDockerApi("api", kernel);
                Assert.NotNull(result);
                Assert.IsType<DockerApiFluentBuilder>(result);
            }
            finally { kernel.Dispose(); }
        }

        [Fact]
        public async Task WithinDockerApi_UseContainer_ReturnsDockerApiBuilder()
        {
            var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("api");
            try
            {
                var result = new Builder()
                    .WithinDockerApi("api", kernel)
                    .UseContainer(c => c.UseImage("alpine:latest").WithName("test"));

                Assert.NotNull(result);
                Assert.IsType<DockerApiFluentBuilder>(result);
            }
            finally { kernel.Dispose(); }
        }

        [Fact]
        public async Task WithinDockerApi_UseNetwork_ReturnsDockerApiBuilder()
        {
            var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("api");
            try
            {
                var result = new Builder()
                    .WithinDockerApi("api", kernel)
                    .UseNetwork(n => n.WithName("api-net"));

                Assert.NotNull(result);
                Assert.IsType<DockerApiFluentBuilder>(result);
            }
            finally { kernel.Dispose(); }
        }

        [Fact]
        public async Task WithinDockerApi_Chaining_Works()
        {
            var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("api");
            try
            {
                var result = new Builder()
                    .WithinDockerApi("api", kernel)
                    .UseNetwork(n => n.WithName("net"))
                    .UseContainer(c => c.UseImage("alpine").WithName("test"));

                Assert.NotNull(result);
                Assert.IsType<DockerApiFluentBuilder>(result);
            }
            finally { kernel.Dispose(); }
        }

        #endregion

        #region WithinPodmanCli

        [Fact]
        public async Task WithinPodmanCli_ReturnsPodmanCliFluentBuilder()
        {
            var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("podman");
            try
            {
                var result = new Builder().WithinPodmanCli("podman", kernel);
                Assert.NotNull(result);
                Assert.IsType<PodmanCliFluentBuilder>(result);
            }
            finally { kernel.Dispose(); }
        }

        [Fact]
        public async Task WithinPodmanCli_UseContainer_ReturnsPodmanCliBuilder()
        {
            var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("podman");
            try
            {
                var result = new Builder()
                    .WithinPodmanCli("podman", kernel)
                    .UseContainer(c => c.UseImage("alpine:latest").WithName("test"));

                Assert.NotNull(result);
                Assert.IsType<PodmanCliFluentBuilder>(result);
            }
            finally { kernel.Dispose(); }
        }

        [Fact]
        public async Task WithinPodmanCli_UsePod_ReturnsPodmanCliBuilder()
        {
            var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("podman");
            try
            {
                var result = new Builder()
                    .WithinPodmanCli("podman", kernel)
                    .UsePod(p => p.WithName("my-pod"));

                Assert.NotNull(result);
                Assert.IsType<PodmanCliFluentBuilder>(result);
            }
            finally { kernel.Dispose(); }
        }

        [Fact]
        public async Task WithinPodmanCli_Chaining_Works()
        {
            var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("podman");
            try
            {
                var result = new Builder()
                    .WithinPodmanCli("podman", kernel)
                    .UseNetwork(n => n.WithName("pod-net"))
                    .UsePod(p => p.WithName("my-pod"))
                    .UseContainer(c => c.UseImage("alpine").WithName("test"));

                Assert.NotNull(result);
                Assert.IsType<PodmanCliFluentBuilder>(result);
            }
            finally { kernel.Dispose(); }
        }

        #endregion

        #region WithinDriver (backward compat)

        [Fact]
        public async Task WithinDriver_UseCompose_StillWorks()
        {
            var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            try
            {
                // UseCompose is still available on Builder (not on IBuilder interface)
                var builder = new Builder()
                    .WithinDriver("docker", kernel)
                    .UseCompose(c => c.WithComposeFile("docker-compose.yml"));

                Assert.NotNull(builder);
                Assert.IsType<Builder>(builder);
            }
            finally { kernel.Dispose(); }
        }

        [Fact]
        public async Task WithinDriver_UsePod_StillWorks()
        {
            var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("podman");
            try
            {
                var builder = new Builder()
                    .WithinDriver("podman", kernel)
                    .UsePod(p => p.WithName("generic-pod"));

                Assert.NotNull(builder);
                Assert.IsType<Builder>(builder);
            }
            finally { kernel.Dispose(); }
        }

        #endregion

        #region Builder Scope Validation

        [Fact]
        public void WithinDockerCli_NullKernelOnFirst_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                new Builder().WithinDockerCli("docker"));
        }

        [Fact]
        public void WithinDockerApi_NullKernelOnFirst_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                new Builder().WithinDockerApi("api"));
        }

        [Fact]
        public void WithinPodmanCli_NullKernelOnFirst_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                new Builder().WithinPodmanCli("podman"));
        }

        #endregion
    }

    /// <summary>
    /// Unit tests for the PodBuilder internal implementation.
    /// </summary>
    [Trait("Category", "Unit")]
    public class PodBuilderTests
    {
        [Fact]
        public void PodBuilder_ImplementsIDriverScopedBuilder()
        {
            var builderType = typeof(Builder).Assembly
                .GetType("FluentDocker.Builders.PodBuilder");
            Assert.NotNull(builderType);
            Assert.True(typeof(IDriverScopedBuilder).IsAssignableFrom(builderType));
        }

        [Fact]
        public void PodBuilder_ImplementsIPodBuilder()
        {
            var builderType = typeof(Builder).Assembly
                .GetType("FluentDocker.Builders.PodBuilder");
            Assert.NotNull(builderType);
            Assert.True(typeof(IPodBuilder).IsAssignableFrom(builderType));
        }

        [Fact]
        public void PodBuilder_WithName_ChainsCorrectly()
        {
            var builder = CreatePodBuilder("podman");
            var result = builder.WithName("test-pod");
            Assert.Same(builder, result);
        }

        [Fact]
        public void PodBuilder_WithPort_ChainsCorrectly()
        {
            var builder = CreatePodBuilder("podman");
            var result = builder.WithPort("8080", "80");
            Assert.Same(builder, result);
        }

        [Fact]
        public void PodBuilder_ExposePort_ChainsCorrectly()
        {
            var builder = CreatePodBuilder("podman");
            var result = builder.ExposePort("3000");
            Assert.Same(builder, result);
        }

        [Fact]
        public void PodBuilder_WithNetwork_ChainsCorrectly()
        {
            var builder = CreatePodBuilder("podman");
            var result = builder.WithNetwork("pod-net");
            Assert.Same(builder, result);
        }

        [Fact]
        public void PodBuilder_WithLabel_ChainsCorrectly()
        {
            var builder = CreatePodBuilder("podman");
            var result = builder.WithLabel("env", "test");
            Assert.Same(builder, result);
        }

        [Fact]
        public void PodBuilder_WithHostname_ChainsCorrectly()
        {
            var builder = CreatePodBuilder("podman");
            var result = builder.WithHostname("my-host");
            Assert.Same(builder, result);
        }

        [Fact]
        public void PodBuilder_RemoveOnDispose_ChainsCorrectly()
        {
            var builder = CreatePodBuilder("podman");
            var result = builder.RemoveOnDispose();
            Assert.Same(builder, result);
        }

        [Fact]
        public void PodBuilder_FullChain_Works()
        {
            var builder = CreatePodBuilder("podman");
            var result = builder
                .WithName("full-test")
                .WithPort("8080", "80")
                .ExposePort("3000")
                .WithNetwork("pod-net")
                .WithLabel("env", "dev")
                .WithHostname("pod-host")
                .RemoveOnDispose();

            Assert.Same(builder, result);
        }

        [Fact]
        public async Task PodBuilder_ExecuteAsync_CallsDriver()
        {
            // Arrange
            var mockPodDriver = new Mock<IPodmanPodDriver>();
            mockPodDriver
                .Setup(d => d.CreatePodAsync(
                    It.IsAny<DriverContext>(),
                    It.IsAny<PodCreateConfig>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CommandResponse<PodCreateResult>.Ok(
                    new PodCreateResult { Id = "pod-123" }));

            var mockPack = new MockDriverPack();
            mockPack.RegisterCustomDriver(mockPodDriver.Object);
            var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync(
                "podman", mockPack);

            try
            {
                var builder = CreatePodBuilder("podman", kernel);
                builder
                    .WithName("test-pod")
                    .WithPort("8080", "80")
                    .WithNetwork("my-net")
                    .WithLabel("app", "web")
                    .WithHostname("web-pod");

                // Act
                var service = await InvokeExecuteAsync(builder);

                // Assert
                Assert.NotNull(service);
                Assert.IsAssignableFrom<Services.IPodService>(service);
                var podService = (Services.IPodService)service;
                Assert.Equal("pod-123", podService.Id);
                Assert.Equal("test-pod", podService.Name);

                mockPodDriver.Verify(d => d.CreatePodAsync(
                    It.IsAny<DriverContext>(),
                    It.Is<PodCreateConfig>(c =>
                        c.Name == "test-pod" &&
                        c.Network == "my-net" &&
                        c.Hostname == "web-pod" &&
                        c.Ports.Count == 1 &&
                        c.Ports[0] == "8080:80" &&
                        c.Labels["app"] == "web"),
                    It.IsAny<CancellationToken>()), Times.Once);
            }
            finally { kernel.Dispose(); }
        }

        [Fact]
        public async Task PodBuilder_ExecuteAsync_DriverFailure_ThrowsDriverException()
        {
            // Arrange
            var mockPodDriver = new Mock<IPodmanPodDriver>();
            mockPodDriver
                .Setup(d => d.CreatePodAsync(
                    It.IsAny<DriverContext>(),
                    It.IsAny<PodCreateConfig>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CommandResponse<PodCreateResult>.Fail("pod creation failed"));

            var mockPack = new MockDriverPack();
            mockPack.RegisterCustomDriver(mockPodDriver.Object);
            var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync(
                "podman", mockPack);

            try
            {
                var builder = CreatePodBuilder("podman", kernel);
                builder.WithName("fail-pod");

                // Act & Assert
                await Assert.ThrowsAsync<DriverException>(
                    () => InvokeExecuteAsync(builder));
            }
            finally { kernel.Dispose(); }
        }

        #region Helpers

        private static IPodBuilder CreatePodBuilder(
            string driverId, FluentDockerKernel kernel = null)
        {
            kernel ??= new FluentDockerKernel();
            var builderType = typeof(Builder).Assembly
                .GetType("FluentDocker.Builders.PodBuilder");
            return (IPodBuilder)Activator.CreateInstance(builderType, kernel, driverId);
        }

        private static async Task<Services.IService> InvokeExecuteAsync(IPodBuilder builder)
        {
            var builderType = builder.GetType();
            var executeMethod = builderType.GetMethod("ExecuteAsync",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(executeMethod);

            var task = (Task<Services.IService>)executeMethod.Invoke(
                builder, new object[] { CancellationToken.None });
            return await task;
        }

        #endregion
    }
}
