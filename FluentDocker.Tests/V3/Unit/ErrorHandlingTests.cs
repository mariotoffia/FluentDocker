using System;
using System.Threading;
using System.Threading.Tasks;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Drivers;
using Xunit;

namespace Ductus.FluentDocker.Tests.V3.UnitTests
{
    /// <summary>
    /// Tests for v3.0.0 exception handling and error propagation.
    /// </summary>
    public class ErrorHandlingTests
    {
        [Fact]
        public void ContainerStartException_WithContainerId_SetsProperties()
        {
            // Arrange
            var context = new ErrorContext
            {
                OperationId = Guid.NewGuid().ToString(),
                DriverId = "docker-local",
                Operation = "StartContainer"
            };

            // Act
            var ex = new ContainerStartException("container-123", "port already in use", context);

            // Assert
            Assert.Equal("container-123", ex.ContainerId);
            Assert.Contains("container-123", ex.Message);
            Assert.Contains("port already in use", ex.Message);
            Assert.Equal(ErrorCodes.Container.StartFailed, ex.ErrorCode);
            Assert.Equal(context, ex.Context);
            Assert.True(ex.IsTransient);
        }

        [Fact]
        public void DriverException_WithErrorContext_PreservesContext()
        {
            // Arrange
            var context = new ErrorContext
            {
                OperationId = "op-123",
                DriverId = "test-driver",
                Operation = "TestOperation",
                Timestamp = DateTime.UtcNow,
                ExitCode = 1
            };

            // Act
            var ex = new DriverException("Test error", ErrorCodes.General.Unknown, context);

            // Assert
            Assert.Equal(context, ex.Context);
            Assert.Equal("op-123", ex.Context.OperationId);
            Assert.Equal("test-driver", ex.Context.DriverId);
            Assert.Equal(1, ex.Context.ExitCode);
        }

        [Fact]
        public void ImageNotFoundException_SetsImageName()
        {
            // Arrange & Act
            var ex = new ImageNotFoundException("nonexistent:latest");

            // Assert
            Assert.Equal("nonexistent:latest", ex.ImageName);
            Assert.Contains("nonexistent:latest", ex.Message);
            Assert.Equal(ErrorCodes.Image.NotFound, ex.ErrorCode);
        }

        [Fact]
        public void ImagePullException_WithReason_FormatsMessage()
        {
            // Arrange & Act
            var ex = new ImagePullException("myimage:v1", "authentication required");

            // Assert
            Assert.Equal("myimage:v1", ex.ImageName);
            Assert.Contains("myimage:v1", ex.Message);
            Assert.Contains("authentication required", ex.Message);
            Assert.Equal(ErrorCodes.Image.PullFailed, ex.ErrorCode);
            Assert.True(ex.IsTransient);
        }

        [Fact]
        public void DriverException_IsTransient_PropagatesCorrectly()
        {
            // Arrange & Act
            var transient = new DriverException("Transient error", ErrorCodes.Network.Timeout, isTransient: true);
            var permanent = new DriverException("Permanent error", ErrorCodes.Container.NotFound, isTransient: false);

            // Assert
            Assert.True(transient.IsTransient);
            Assert.False(permanent.IsTransient);
        }

        [Fact]
        public void DriverException_WithInnerException_Wraps()
        {
            // Arrange
            var inner = new InvalidOperationException("Inner error");

            // Act
            var ex = new DriverException("Outer error", ErrorCodes.General.Unknown, inner);

            // Assert
            Assert.Same(inner, ex.InnerException);
            Assert.Equal("Inner error", ex.InnerException.Message);
        }

        [Fact]
        public void ErrorContext_ToString_FormatsReadably()
        {
            // Arrange
            var context = new ErrorContext
            {
                OperationId = "op-456",
                DriverId = "docker-cli",
                Operation = "CreateContainer",
                Timestamp = new DateTime(2025, 11, 15, 10, 30, 0, DateTimeKind.Utc),
                ExitCode = 125,
                StdOut = "Container output",
                StdErr = "Error details"
            };

            // Act
            var str = context.ToString();

            // Assert
            Assert.Contains("op-456", str);
            Assert.Contains("docker-cli", str);
            Assert.Contains("CreateContainer", str);
            Assert.Contains("125", str);
        }

        [Fact]
        public void CommandResponse_Fail_CreatesFailedResponse()
        {
            // Arrange
            var context = new ErrorContext
            {
                OperationId = "test-op",
                DriverId = "test-driver"
            };

            // Act
            var response = CommandResponse<string>.Fail(
                "Operation failed",
                ErrorCodes.Container.CreateFailed,
                context,
                exitCode: 1);

            // Assert
            Assert.False(response.Success);
            Assert.Equal("Operation failed", response.Error);
            Assert.Equal(ErrorCodes.Container.CreateFailed, response.ErrorCode);
            Assert.Equal(context, response.ErrorContext);
            Assert.Equal(1, response.ExitCode);
            Assert.Null(response.Data);
        }

        [Fact]
        public void CommandResponse_Ok_CreatesSuccessResponse()
        {
            // Arrange
            var data = "Success data";

            // Act
            var response = CommandResponse<string>.Ok(data);

            // Assert
            Assert.True(response.Success);
            Assert.Equal(data, response.Data);
            Assert.Null(response.Error);
            Assert.Null(response.ErrorCode);
            Assert.Null(response.ErrorContext);
        }

        [Fact]
        public void ErrorCodes_Hierarchy_FollowsConvention()
        {
            // Act & Assert - Container errors
            Assert.StartsWith("CONTAINER.", ErrorCodes.Container.NotFound);
            Assert.StartsWith("CONTAINER.", ErrorCodes.Container.CreateFailed);
            Assert.StartsWith("CONTAINER.", ErrorCodes.Container.StartFailed);

            // Image errors
            Assert.StartsWith("IMAGE.", ErrorCodes.Image.NotFound);
            Assert.StartsWith("IMAGE.", ErrorCodes.Image.PullFailed);

            // Network errors
            Assert.StartsWith("NETWORK.", ErrorCodes.Network.NotFound);
            Assert.StartsWith("NETWORK.", ErrorCodes.Network.CreateFailed);

            // Volume errors
            Assert.StartsWith("VOLUME.", ErrorCodes.Volume.NotFound);
            Assert.StartsWith("VOLUME.", ErrorCodes.Volume.CreateFailed);
        }

        [Fact]
        public void ErrorCodes_AreUnique()
        {
            // Arrange
            var codes = new[]
            {
                ErrorCodes.General.Unknown,
                ErrorCodes.Container.NotFound,
                ErrorCodes.Container.CreateFailed,
                ErrorCodes.Image.NotFound,
                ErrorCodes.Image.PullFailed,
                ErrorCodes.Network.NotFound,
                ErrorCodes.Volume.NotFound
            };

            // Act & Assert
            var uniqueCodes = new System.Collections.Generic.HashSet<string>(codes);
            Assert.Equal(codes.Length, uniqueCodes.Count);
        }

        [Fact]
        public async Task ExceptionPropagation_ThroughAsyncChain_Preserves()
        {
            // Arrange
            var context = new ErrorContext { OperationId = "test" };

            async Task ThrowAsync()
            {
                await Task.Delay(1);
                throw new ContainerStartException("container-id", "test error", context);
            }

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ContainerStartException>(async () => await ThrowAsync());
            Assert.Equal("container-id", ex.ContainerId);
            Assert.Equal(context, ex.Context);
        }

        [Fact]
        public void CancellationToken_Cancellation_ThrowsOperationCanceledException()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            Assert.Throws<OperationCanceledException>(() =>
                cts.Token.ThrowIfCancellationRequested());
        }
    }
}
