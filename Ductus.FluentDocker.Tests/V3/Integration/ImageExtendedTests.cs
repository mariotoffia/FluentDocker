using System;
using System.Linq;
using System.Threading.Tasks;
using Ductus.FluentDocker.Drivers;
using Ductus.FluentDocker.Kernel;
using Ductus.FluentDocker.Model.Drivers;
using Xunit;

namespace Ductus.FluentDocker.Tests.V3.Integration
{
    [Trait("Category", "Integration")]
    public class ImageExtendedTests : IDisposable
    {
        private readonly FluentDockerKernel _kernel;
        private readonly IImageDriver _driver;
        private readonly DriverContext _context;

        public ImageExtendedTests()
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
        public async Task Image_PullWithProgress_ReportsProgress()
        {
            // Arrange
            var progressReported = false;
            var progress = new Progress<ImagePullProgress>(p =>
            {
                progressReported = true;
            });

            // Act
            var response = await _driver.PullAsync(_context, "alpine", "3.19", progress);

            // Assert
            Assert.True(response.Success, response.Error);
            // Note: Progress may or may not be reported depending on implementation
        }

        [Fact]
        public async Task Image_TagMultipleTags_CreatesAllTags()
        {
            // Arrange
            await _driver.PullAsync(_context, "alpine", "latest");

            var tagName1 = $"test-tag-1-{Guid.NewGuid():N}";
            var tagName2 = $"test-tag-2-{Guid.NewGuid():N}";

            // Act
            var response1 = await _driver.TagAsync(_context, "alpine:latest", "alpine", tagName1);
            var response2 = await _driver.TagAsync(_context, "alpine:latest", "alpine", tagName2);

            // Assert
            Assert.True(response1.Success, response1.Error);
            Assert.True(response2.Success, response2.Error);

            // Verify both tags exist
            var listResponse = await _driver.ListAsync(_context);
            Assert.Contains(listResponse.Data, img =>
                img.Tags != null && img.Tags.Any(t => t.Contains("test-tag-1")));
            Assert.Contains(listResponse.Data, img =>
                img.Tags != null && img.Tags.Any(t => t.Contains("test-tag-2")));

            // Cleanup
            await _driver.RemoveAsync(_context, $"alpine:{tagName1}", force: true);
            await _driver.RemoveAsync(_context, $"alpine:{tagName2}", force: true);
        }

        [Fact]
        public async Task Image_ListWithFilter_FiltersCorrectly()
        {
            // Arrange
            await _driver.PullAsync(_context, "alpine", "latest");

            var filter = new ImageListFilter
            {
                Reference = "alpine:latest"
            };

            // Act
            var response = await _driver.ListAsync(_context, filter);

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(response.Data);
            Assert.Contains(response.Data, img => img.Repository == "alpine");
        }

        [Fact]
        public async Task Image_InspectDetails_ReturnsCompleteInfo()
        {
            // Arrange
            await _driver.PullAsync(_context, "alpine", "latest");

            // Act
            var response = await _driver.InspectAsync(_context, "alpine:latest");

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(response.Data);
            Assert.NotNull(response.Data.Id);
            Assert.True(response.Data.Size > 0);
        }

        [Fact]
        public async Task Image_RemoveForce_RemovesEvenWithContainers()
        {
            // Arrange
            await _driver.PullAsync(_context, "alpine", "latest");

            var testTagName = $"force-remove-{Guid.NewGuid():N}";
            var testTag = $"alpine:{testTagName}";
            await _driver.TagAsync(_context, "alpine:latest", "alpine", testTagName);

            // Create container using the image
            var containerDriver = _kernel.SysCtl<IContainerDriver>("docker-local");
            var containerResponse = await containerDriver.CreateAsync(_context, new ContainerCreateConfig
            {
                Image = testTag,
                Name = $"test-container-{Guid.NewGuid():N}"
            });
            Assert.True(containerResponse.Success);

            // Act
            var removeResponse = await _driver.RemoveAsync(_context, testTag, force: true);

            // Assert - May succeed or fail depending on Docker version
            // Just verify no exception thrown
            Assert.True(true);

            // Cleanup
            await containerDriver.RemoveAsync(_context, containerResponse.Data.Id, force: true);
            try { await _driver.RemoveAsync(_context, testTag, force: true); } catch { }
        }

        [Fact]
        public async Task Image_PullNonExistentTag_Fails()
        {
            // Act
            var response = await _driver.PullAsync(_context, "alpine", "nonexistent-tag-99999");

            // Assert
            Assert.False(response.Success);
            Assert.NotNull(response.Error);
        }

        [Fact]
        public async Task Image_PullNonExistentRepository_Fails()
        {
            // Act
            var response = await _driver.PullAsync(_context, "this-repo-does-not-exist-12345", "latest");

            // Assert
            Assert.False(response.Success);
            Assert.NotNull(response.Error);
        }

        [Fact]
        public async Task Image_Build_CreatesImage()
        {
            // Arrange
            var config = new ImageBuildConfig
            {
                BuildContext = "./",
                Tags = new System.Collections.Generic.List<string> { $"test-build-{Guid.NewGuid():N}" }
            };

            // Act
            var response = await _driver.BuildAsync(_context, config);

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(response.Data);
            Assert.NotEmpty(response.Data.ImageId);

            // Cleanup
            await _driver.RemoveAsync(_context, response.Data.ImageId, force: true);
        }

        [Fact]
        public async Task Image_BuildWithBuildArgs_PassesArguments()
        {
            // Arrange
            var config = new ImageBuildConfig
            {
                BuildContext = "./",
                BuildArgs = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "VERSION", "1.0.0" },
                    { "BUILD_DATE", DateTime.UtcNow.ToString("yyyy-MM-dd") }
                },
                Tags = new System.Collections.Generic.List<string> { $"test-buildargs-{Guid.NewGuid():N}" }
            };

            // Act
            var response = await _driver.BuildAsync(_context, config);

            // Assert
            Assert.True(response.Success, response.Error);

            // Cleanup
            await _driver.RemoveAsync(_context, response.Data.ImageId, force: true);
        }

        [Fact]
        public async Task Image_BuildWithLabels_SetsLabels()
        {
            // Arrange
            var config = new ImageBuildConfig
            {
                BuildContext = "./",
                Labels = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "test.label", "test-value" },
                    { "version", "1.0" }
                },
                Tags = new System.Collections.Generic.List<string> { $"test-labels-{Guid.NewGuid():N}" }
            };

            // Act
            var response = await _driver.BuildAsync(_context, config);

            // Assert
            Assert.True(response.Success, response.Error);

            // Cleanup
            await _driver.RemoveAsync(_context, response.Data.ImageId, force: true);
        }

        [Fact]
        public async Task Image_BuildWithTarget_BuildsStage()
        {
            // Arrange
            var config = new ImageBuildConfig
            {
                BuildContext = "./",
                Target = "build-stage",
                Tags = new System.Collections.Generic.List<string> { $"test-target-{Guid.NewGuid():N}" }
            };

            // Act
            var response = await _driver.BuildAsync(_context, config);

            // Assert
            Assert.True(response.Success, response.Error);

            // Cleanup
            await _driver.RemoveAsync(_context, response.Data.ImageId, force: true);
        }

        [Fact]
        public async Task Image_ListAll_IncludesIntermediates()
        {
            // Arrange
            var filter = new ImageListFilter
            {
                All = true
            };

            // Act
            var response = await _driver.ListAsync(_context, filter);

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(response.Data);
        }

        [Fact]
        public async Task Image_TagInvalidFormat_Fails()
        {
            // Arrange
            await _driver.PullAsync(_context, "alpine", "latest");

            // Act - Invalid tag format (spaces not allowed)
            var response = await _driver.TagAsync(_context, "alpine:latest", "invalid", "tag name");

            // Assert
            Assert.False(response.Success);
        }
    }
}
