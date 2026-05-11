using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Containers;
using FluentDocker.Services;
using FluentDocker.Services.Extensions;
using Moq;
using Xunit;


namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Unit tests for <see cref="ServiceExtensions"/>.
  /// Tests for port resolution, configuration, and host address methods.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class ServiceExtensionsTests
  {
    #region Helpers

    /// <summary>
    /// Creates a mock <see cref="IContainerService"/> whose <c>InspectAsync</c>
    /// returns the supplied <see cref="Container"/>.
    /// </summary>
    private static Mock<IContainerService> CreateContainerServiceMock(Container container)
    {
      var mock = new Mock<IContainerService>();
      mock.Setup(s => s.Id).Returns("test-container-id");
      mock.Setup(s => s.Name).Returns("test-container");
      mock.Setup(s => s.InspectAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(container);
      return mock;
    }

    /// <summary>
    /// Builds a <see cref="Container"/> with the specified port bindings in
    /// <see cref="ContainerNetworkSettings.Ports"/>.
    /// </summary>
    private static Container CreateContainerWithPorts(
        Dictionary<string, HostIpEndpoint[]> ports)
    {
      return new Container
      {
        Id = "test-container-id",
        Name = "test-container",
        NetworkSettings = new ContainerNetworkSettings
        {
          Ports = ports
        }
      };
    }

    /// <summary>
    /// Builds a single <see cref="HostIpEndpoint"/> with the specified host IP and port.
    /// </summary>
    private static HostIpEndpoint CreateBinding(string hostIp, string hostPort)
    {
      return new HostIpEndpoint
      {
        HostIp = hostIp,
        HostPort = hostPort
      };
    }

    #endregion

    #region ToHostExposedEndpointAsync

    [Fact]
    public async Task ToHostExposedEndpointAsync_PortFound_ReturnsCorrectEndpoint()
    {
      // Arrange
      var ports = new Dictionary<string, HostIpEndpoint[]>
      {
        ["5432/tcp"] = [CreateBinding("192.168.1.10", "32768")]
      };
      var container = CreateContainerWithPorts(ports);
      var mock = CreateContainerServiceMock(container);

      // Act
      var endpoint = await mock.Object.ToHostExposedEndpointAsync("5432/tcp", TestContext.Current.CancellationToken);

      // Assert
      Assert.NotNull(endpoint);
      Assert.Equal(32768, endpoint.Port);
      Assert.Equal(IPAddress.Parse("192.168.1.10"), endpoint.Address);
    }

    [Fact]
    public async Task ToHostExposedEndpointAsync_ZeroAddress_ResolvesToLocalhost()
    {
      // Arrange
      var ports = new Dictionary<string, HostIpEndpoint[]>
      {
        ["8080/tcp"] = [CreateBinding("0.0.0.0", "9090")]
      };
      var container = CreateContainerWithPorts(ports);
      var mock = CreateContainerServiceMock(container);

      // Act
      var endpoint = await mock.Object.ToHostExposedEndpointAsync("8080/tcp", TestContext.Current.CancellationToken);

      // Assert
      Assert.NotNull(endpoint);
      Assert.Equal(IPAddress.Parse("127.0.0.1"), endpoint.Address);
      Assert.Equal(9090, endpoint.Port);
    }

    [Fact]
    public async Task ToHostExposedEndpointAsync_EmptyHostIp_ResolvesToLocalhost()
    {
      // Arrange
      var ports = new Dictionary<string, HostIpEndpoint[]>
      {
        ["3306/tcp"] = [CreateBinding("", "33060")]
      };
      var container = CreateContainerWithPorts(ports);
      var mock = CreateContainerServiceMock(container);

      // Act
      var endpoint = await mock.Object.ToHostExposedEndpointAsync("3306/tcp", TestContext.Current.CancellationToken);

      // Assert
      Assert.NotNull(endpoint);
      Assert.Equal(IPAddress.Parse("127.0.0.1"), endpoint.Address);
      Assert.Equal(33060, endpoint.Port);
    }

    [Fact]
    public async Task ToHostExposedEndpointAsync_PortNotFound_ReturnsNull()
    {
      // Arrange
      var ports = new Dictionary<string, HostIpEndpoint[]>
      {
        ["5432/tcp"] = [CreateBinding("127.0.0.1", "32768")]
      };
      var container = CreateContainerWithPorts(ports);
      var mock = CreateContainerServiceMock(container);

      // Act
      var endpoint = await mock.Object.ToHostExposedEndpointAsync("9999/tcp", TestContext.Current.CancellationToken);

      // Assert
      Assert.Null(endpoint);
    }

    [Fact]
    public async Task ToHostExposedEndpointAsync_NoNetworkSettings_ReturnsNull()
    {
      // Arrange
      var container = new Container
      {
        Id = "test-container-id",
        Name = "test-container",
        NetworkSettings = null
      };
      var mock = CreateContainerServiceMock(container);

      // Act
      var endpoint = await mock.Object.ToHostExposedEndpointAsync("5432/tcp", TestContext.Current.CancellationToken);

      // Assert
      Assert.Null(endpoint);
    }

    [Fact]
    public async Task ToHostExposedEndpointAsync_NullPorts_ReturnsNull()
    {
      // Arrange
      var container = new Container
      {
        Id = "test-container-id",
        Name = "test-container",
        NetworkSettings = new ContainerNetworkSettings { Ports = null }
      };
      var mock = CreateContainerServiceMock(container);

      // Act
      var endpoint = await mock.Object.ToHostExposedEndpointAsync("5432/tcp", TestContext.Current.CancellationToken);

      // Assert
      Assert.Null(endpoint);
    }

    [Fact]
    public async Task ToHostExposedEndpointAsync_EmptyBindings_ReturnsNull()
    {
      // Arrange
      var ports = new Dictionary<string, HostIpEndpoint[]>
      {
        ["5432/tcp"] = []
      };
      var container = CreateContainerWithPorts(ports);
      var mock = CreateContainerServiceMock(container);

      // Act
      var endpoint = await mock.Object.ToHostExposedEndpointAsync("5432/tcp", TestContext.Current.CancellationToken);

      // Assert
      Assert.Null(endpoint);
    }

    [Fact]
    public async Task ToHostExposedEndpointAsync_NullBindingsArray_ReturnsNull()
    {
      // Arrange
      var ports = new Dictionary<string, HostIpEndpoint[]>
      {
        ["5432/tcp"] = null
      };
      var container = CreateContainerWithPorts(ports);
      var mock = CreateContainerServiceMock(container);

      // Act
      var endpoint = await mock.Object.ToHostExposedEndpointAsync("5432/tcp", TestContext.Current.CancellationToken);

      // Assert
      Assert.Null(endpoint);
    }

    [Fact]
    public async Task ToHostExposedEndpointAsync_NullContainer_ReturnsNull()
    {
      // Arrange
      var mock = CreateContainerServiceMock(null);

      // Act
      var endpoint = await mock.Object.ToHostExposedEndpointAsync("5432/tcp", TestContext.Current.CancellationToken);

      // Assert
      Assert.Null(endpoint);
    }

    [Theory]
    [InlineData("5432/tcp", "192.168.1.5", "5432", "192.168.1.5", 5432)]
    [InlineData("80/tcp", "10.0.0.1", "8080", "10.0.0.1", 8080)]
    [InlineData("443/tcp", "0.0.0.0", "44300", "127.0.0.1", 44300)]
    public async Task ToHostExposedEndpointAsync_VariousBindings_ReturnsExpected(
        string portAndProto,
        string bindIp,
        string bindPort,
        string expectedIp,
        int expectedPort)
    {
      // Arrange
      var ports = new Dictionary<string, HostIpEndpoint[]>
      {
        [portAndProto] = [CreateBinding(bindIp, bindPort)]
      };
      var container = CreateContainerWithPorts(ports);
      var mock = CreateContainerServiceMock(container);

      // Act
      var endpoint = await mock.Object.ToHostExposedEndpointAsync(portAndProto, TestContext.Current.CancellationToken);

      // Assert
      Assert.NotNull(endpoint);
      Assert.Equal(IPAddress.Parse(expectedIp), endpoint.Address);
      Assert.Equal(expectedPort, endpoint.Port);
    }

    [Fact]
    public async Task ToHostExposedEndpointAsync_MultipleBindings_ReturnsFirst()
    {
      // Arrange - Docker may return multiple bindings for a port
      var ports = new Dictionary<string, HostIpEndpoint[]>
      {
        ["80/tcp"] =
        [
          CreateBinding("127.0.0.1", "8080"),
          CreateBinding("192.168.1.10", "8081")
        ]
      };
      var container = CreateContainerWithPorts(ports);
      var mock = CreateContainerServiceMock(container);

      // Act
      var endpoint = await mock.Object.ToHostExposedEndpointAsync("80/tcp", TestContext.Current.CancellationToken);

      // Assert
      Assert.NotNull(endpoint);
      Assert.Equal(IPAddress.Parse("127.0.0.1"), endpoint.Address);
      Assert.Equal(8080, endpoint.Port);
    }

    #endregion

    #region GetHostPortAsync

    [Fact]
    public async Task GetHostPortAsync_PortFound_ReturnsPortNumber()
    {
      // Arrange
      var ports = new Dictionary<string, HostIpEndpoint[]>
      {
        ["5432/tcp"] = [CreateBinding("127.0.0.1", "32768")]
      };
      var container = CreateContainerWithPorts(ports);
      var mock = CreateContainerServiceMock(container);

      // Act
      var port = await mock.Object.GetHostPortAsync("5432/tcp", TestContext.Current.CancellationToken);

      // Assert
      Assert.Equal(32768, port);
    }

    [Fact]
    public async Task GetHostPortAsync_PortNotFound_ReturnsZero()
    {
      // Arrange
      var ports = new Dictionary<string, HostIpEndpoint[]>
      {
        ["5432/tcp"] = [CreateBinding("127.0.0.1", "32768")]
      };
      var container = CreateContainerWithPorts(ports);
      var mock = CreateContainerServiceMock(container);

      // Act
      var port = await mock.Object.GetHostPortAsync("9999/tcp", TestContext.Current.CancellationToken);

      // Assert
      Assert.Equal(0, port);
    }

    [Fact]
    public async Task GetHostPortAsync_NoNetworkSettings_ReturnsZero()
    {
      // Arrange
      var container = new Container
      {
        Id = "test-container-id",
        NetworkSettings = null
      };
      var mock = CreateContainerServiceMock(container);

      // Act
      var port = await mock.Object.GetHostPortAsync("5432/tcp", TestContext.Current.CancellationToken);

      // Assert
      Assert.Equal(0, port);
    }

    [Fact]
    public async Task GetHostPortAsync_MultiplePorts_EachResolvesCorrectly()
    {
      // Arrange
      var ports = new Dictionary<string, HostIpEndpoint[]>
      {
        ["80/tcp"] = [CreateBinding("127.0.0.1", "8080")],
        ["443/tcp"] = [CreateBinding("127.0.0.1", "8443")]
      };
      var container = CreateContainerWithPorts(ports);
      var mock = CreateContainerServiceMock(container);

      // Act
      var httpPort = await mock.Object.GetHostPortAsync("80/tcp", TestContext.Current.CancellationToken);
      var httpsPort = await mock.Object.GetHostPortAsync("443/tcp", TestContext.Current.CancellationToken);

      // Assert
      Assert.Equal(8080, httpPort);
      Assert.Equal(8443, httpsPort);
    }

    #endregion

    #region GetConfigurationAsync

    [Fact]
    public async Task GetConfigurationAsync_DelegatesToInspectAsync()
    {
      // Arrange
      var expectedContainer = new Container
      {
        Id = "abc123",
        Name = "my-container",
        Image = "nginx:latest"
      };
      var mock = CreateContainerServiceMock(expectedContainer);

      // Act
      var result = await mock.Object.GetConfigurationAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      Assert.NotNull(result);
      Assert.Equal("abc123", result.Id);
      Assert.Equal("my-container", result.Name);
      Assert.Equal("nginx:latest", result.Image);
      mock.Verify(s => s.InspectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetConfigurationAsync_FreshParameter_StillDelegatesToInspect()
    {
      // Arrange - fresh parameter is passed but InspectAsync is always called
      var expectedContainer = new Container { Id = "abc123" };
      var mock = CreateContainerServiceMock(expectedContainer);

      // Act
      var result = await mock.Object.GetConfigurationAsync(fresh: true, TestContext.Current.CancellationToken);

      // Assert
      Assert.NotNull(result);
      Assert.Equal("abc123", result.Id);
      mock.Verify(s => s.InspectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetDockerHost

    [Fact]
    public void GetDockerHost_NativeHost_ReturnsLocalhost()
    {
      // Arrange
      var mock = new Mock<IHostService>();
      mock.Setup(s => s.IsNative).Returns(true);

      // Act
      var host = mock.Object.GetDockerHost();

      // Assert
      Assert.Equal("127.0.0.1", host);
    }

    [Fact]
    public void GetDockerHost_NonNativeHost_ReturnsLocalhost()
    {
      // Arrange
      var mock = new Mock<IHostService>();
      mock.Setup(s => s.IsNative).Returns(false);

      // Act
      var host = mock.Object.GetDockerHost();

      // Assert
      Assert.Equal("127.0.0.1", host);
    }

    #endregion
  }
}
