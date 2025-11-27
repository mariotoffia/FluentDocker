using Ductus.FluentDocker.Model.Drivers;
using Xunit;

namespace Ductus.FluentDocker.Tests.V3.UnitTests
{
    [Trait("Category", "Unit")]
    public class ErrorContextTests
    {
        [Fact]
        public void Constructor_Default_CreatesContext()
        {
            // Act
            var context = new ErrorContext();

            // Assert
            Assert.NotNull(context);
            Assert.NotNull(context.Metadata);
            Assert.NotEqual(default(System.DateTime), context.Timestamp);
        }

        [Fact]
        public void Constructor_WithOperation_SetsOperation()
        {
            // Act
            var context = new ErrorContext("TestOperation");

            // Assert
            Assert.Equal("TestOperation", context.Operation);
        }

        [Fact]
        public void Properties_SetAndGet()
        {
            // Act
            var context = new ErrorContext
            {
                OperationId = "op-123",
                DriverId = "docker-1",
                Host = "tcp://localhost:2376",
                Operation = "CreateContainer",
                ExitCode = 127,
                StdOut = "standard output",
                StdErr = "standard error"
            };

            // Assert
            Assert.Equal("op-123", context.OperationId);
            Assert.Equal("docker-1", context.DriverId);
            Assert.Equal("tcp://localhost:2376", context.Host);
            Assert.Equal("CreateContainer", context.Operation);
            Assert.Equal(127, context.ExitCode);
            Assert.Equal("standard output", context.StdOut);
            Assert.Equal("standard error", context.StdErr);
        }

        [Fact]
        public void ToString_FormatsContext()
        {
            // Arrange
            var context = new ErrorContext("CreateContainer")
            {
                DriverId = "docker",
                Host = "localhost",
                ExitCode = 1
            };

            // Act
            var result = context.ToString();

            // Assert
            Assert.Contains("Operation: CreateContainer", result);
            Assert.Contains("Driver: docker", result);
            Assert.Contains("Host: localhost", result);
            Assert.Contains("ExitCode: 1", result);
        }

        [Fact]
        public void Metadata_CanStoreCustomData()
        {
            // Arrange
            var context = new ErrorContext();

            // Act
            context.Metadata["key1"] = "value1";
            context.Metadata["key2"] = "value2";

            // Assert
            Assert.Equal("value1", context.Metadata["key1"]);
            Assert.Equal("value2", context.Metadata["key2"]);
            Assert.Equal(2, context.Metadata.Count);
        }

        [Fact]
        public void Timestamp_IsSetAutomatically()
        {
            // Arrange
            var before = System.DateTime.UtcNow;

            // Act
            var context = new ErrorContext();
            var after = System.DateTime.UtcNow;

            // Assert
            Assert.InRange(context.Timestamp, before, after.AddSeconds(1));
        }
    }
}
