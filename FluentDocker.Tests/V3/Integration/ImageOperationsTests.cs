using System;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.V3.Integration
{
    [Trait("Category", "Integration")]
    public class ImageOperationsTests : IDisposable
    {
        private readonly FluentDockerKernel _kernel;
        private readonly IImageDriver _driver;
        private readonly DriverContext _context;

        public ImageOperationsTests()
        {
            _kernel = new KernelBuilder()
                .WithDriver("docker-local", b => b.UseDockerCli())
                .BuildAsync()
                .GetAwaiter()
                .GetResult();

            _driver = _kernel.SysCtl<IImageDriver>("docker-local");
            _context = new DriverContext("docker-local");
        }

        public void Dispose()
        {
            _kernel?.Dispose();
        }

        [Fact]
        public async Task PullImage_ValidImage_Success()
        {
            // Act
            var response = await _driver.PullAsync(_context, "alpine", "latest");

            // Assert
            Assert.True(response.Success, response.Error);
        }

        [Fact]
        public async Task ListImages_AfterPull_ContainsImage()
        {
            // Arrange
            await _driver.PullAsync(_context, "alpine", "latest");

            // Act
            var listResponse = await _driver.ListAsync(_context);

            // Assert
            Assert.True(listResponse.Success, listResponse.Error);
            Assert.NotNull(listResponse.Data);
            Assert.Contains(listResponse.Data, img =>
                img.Repository == "alpine" || img.Repository.Contains("alpine"));
        }

        [Fact]
        public async Task InspectImage_ExistingImage_ReturnsDetails()
        {
            // Arrange
            await _driver.PullAsync(_context, "alpine", "latest");

            // Act
            var inspectResponse = await _driver.InspectAsync(_context, "alpine:latest");

            // Assert
            Assert.True(inspectResponse.Success, inspectResponse.Error);
            Assert.NotNull(inspectResponse.Data);
            Assert.NotNull(inspectResponse.Data.Id);
        }

        [Fact]
        public async Task TagImage_ExistingImage_CreatesTag()
        {
            // Arrange
            await _driver.PullAsync(_context, "alpine", "latest");
            var tagName = $"test-{Guid.NewGuid():N}";
            var newTag = $"alpine:{tagName}";

            // Act
            var tagResponse = await _driver.TagAsync(_context, "alpine:latest", "alpine", tagName);

            // Assert
            Assert.True(tagResponse.Success, tagResponse.Error);

            // Verify tag exists
            var listResponse = await _driver.ListAsync(_context);
            Assert.Contains(listResponse.Data, img =>
                img.Tags != null && img.Tags.Any(t => t.Contains("test-")));

            // Cleanup
            await _driver.RemoveAsync(_context, newTag, force: true);
        }

        [Fact]
        public async Task RemoveImage_ExistingImage_Removes()
        {
            // Arrange
            var testTagName = $"test-remove-{Guid.NewGuid():N}";
            var testTag = $"alpine:{testTagName}";
            await _driver.PullAsync(_context, "alpine", "latest");
            await _driver.TagAsync(_context, "alpine:latest", "alpine", testTagName);

            // Act
            var removeResponse = await _driver.RemoveAsync(_context, testTag, force: true);

            // Assert
            Assert.True(removeResponse.Success, removeResponse.Error);
        }

        [Fact]
        public async Task PullImage_InvalidImage_Fails()
        {
            // Act
            var response = await _driver.PullAsync(_context, "this-image-does-not-exist-12345", "latest");

            // Assert
            Assert.False(response.Success);
            Assert.NotNull(response.Error);
        }

        [Fact]
        public async Task InspectImage_NonExistentImage_Fails()
        {
            // Act
            var response = await _driver.InspectAsync(_context, "non-existent-image-12345:latest");

            // Assert
            Assert.False(response.Success);
            Assert.NotNull(response.Error);
        }
    }
}
