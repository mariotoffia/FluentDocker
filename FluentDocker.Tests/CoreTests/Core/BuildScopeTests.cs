using System.Linq;
using FluentDocker.Kernel;
using FluentDocker.Model.Kernel;
using FluentDocker.Services;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Core
{
    /// <summary>
    /// Tests for BuildScope class.
    /// </summary>
    [Trait("Category", "Unit")]
    public class BuildScopeTests
    {
        [Fact]
        public void Constructor_CreatesScope()
        {
            // Arrange
            var kernel = new FluentDockerKernel();

            // Act
            var scope = new BuildScope(kernel, "docker");

            // Assert
            Assert.Equal(kernel, scope.Kernel);
            Assert.Equal("docker", scope.DriverId);
            Assert.Empty(scope.Results);

            // Cleanup
            kernel.Dispose();
        }

        [Fact]
        public void AddResult_AddsService()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var scope = new BuildScope(kernel, "docker");
            var service = new MockService("test");

            // Act
            scope.AddResult(service);

            // Assert
            Assert.Single(scope.Results);
            Assert.Equal(service, scope.Results.First());

            // Cleanup
            kernel.Dispose();
        }

        [Fact]
        public void AddResult_MultipleServices_AllAdded()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var scope = new BuildScope(kernel, "docker");
            var service1 = new MockService("service1");
            var service2 = new MockService("service2");
            var service3 = new MockService("service3");

            // Act
            scope.AddResult(service1);
            scope.AddResult(service2);
            scope.AddResult(service3);

            // Assert
            Assert.Equal(3, scope.Results.Count);
            Assert.Contains(service1, scope.Results);
            Assert.Contains(service2, scope.Results);
            Assert.Contains(service3, scope.Results);

            // Cleanup
            kernel.Dispose();
        }

        [Fact]
        public void AddResult_NullService_NotAdded()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var scope = new BuildScope(kernel, "docker");

            // Act
            scope.AddResult(null);

            // Assert
            Assert.Empty(scope.Results);

            // Cleanup
            kernel.Dispose();
        }

        [Fact]
        public void Results_IsReadOnly()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var scope = new BuildScope(kernel, "docker");
            var service = new MockService("test");
            scope.AddResult(service);

            // Act
            var results = scope.Results;

            // Assert
            Assert.IsAssignableFrom<System.Collections.Generic.IReadOnlyList<IService>>(results);

            // Cleanup
            kernel.Dispose();
        }

        // Mock service for testing
        private class MockService : IService
        {
            public MockService(string name)
            {
                Name = name;
            }

            public string Name { get; }
            public ServiceRunningState State => ServiceRunningState.Unknown;

            public void Start() { }
            public void Pause() { }
            public void Stop() { }
            public void Remove(bool force = false) { }
            public IService AddHook(ServiceRunningState state, System.Action<IService> hook, string uniqueName = null) => this;
            public IService RemoveHook(string uniqueName) => this;
#pragma warning disable CS0067
            public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CS0067
            public void Dispose() { }
        }
    }
}

