using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Regression tests for HostService.CreateNetworkAsync covering the v3 contract
  /// where the parameter type is the canonical FluentDocker.Drivers.NetworkCreateConfig.
  /// These verify the post-A3-refactor behavior: caller-supplied fields
  /// (Subnet, Gateway, Driver, etc.) flow through to the driver unchanged,
  /// the name parameter always overrides any name on the config,
  /// and a null config is replaced with sensible defaults.
  /// </summary>
  [Trait("Category", "Unit")]
  public class HostServiceCreateNetworkTests
  {
    [Fact]
    public async Task CreateNetworkAsync_WithConfig_PassesAllFieldsToDriver()
    {
      var mockPack = new MockDriverPack();
      NetworkCreateConfig captured = null;
      mockPack.NetworkDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<NetworkCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, NetworkCreateConfig, CancellationToken>(
              (_, cfg, _) => captured = cfg)
          .ReturnsAsync(CommandResponse<NetworkCreateResult>.Ok(
              new NetworkCreateResult { Id = "net-id" }));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var config = new NetworkCreateConfig
        {
          Driver = "overlay",
          Subnet = "10.0.0.0/24",
          Gateway = "10.0.0.1",
          EnableIPv6 = true,
          Internal = true,
          Labels = new Dictionary<string, string> { { "env", "test" } },
          Options = new Dictionary<string, string> { { "key", "value" } }
        };

        await service.CreateNetworkAsync(
            "feature-net",
            config,
            TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("feature-net", captured.Name);
        Assert.Equal("overlay", captured.Driver);
        Assert.Equal("10.0.0.0/24", captured.Subnet);
        Assert.Equal("10.0.0.1", captured.Gateway);
        Assert.True(captured.EnableIPv6);
        Assert.True(captured.Internal);
        Assert.Equal("test", captured.Labels["env"]);
        Assert.Equal("value", captured.Options["key"]);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task CreateNetworkAsync_NullConfig_UsesDefaultsAndAppliesName()
    {
      var mockPack = new MockDriverPack();
      NetworkCreateConfig captured = null;
      mockPack.NetworkDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<NetworkCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, NetworkCreateConfig, CancellationToken>(
              (_, cfg, _) => captured = cfg)
          .ReturnsAsync(CommandResponse<NetworkCreateResult>.Ok(
              new NetworkCreateResult { Id = "net-id" }));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");

        await service.CreateNetworkAsync(
            "default-net",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("default-net", captured.Name);
        Assert.Equal("bridge", captured.Driver);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task CreateNetworkAsync_NameParameter_OverridesConfigName()
    {
      var mockPack = new MockDriverPack();
      NetworkCreateConfig captured = null;
      mockPack.NetworkDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<NetworkCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, NetworkCreateConfig, CancellationToken>(
              (_, cfg, _) => captured = cfg)
          .ReturnsAsync(CommandResponse<NetworkCreateResult>.Ok(
              new NetworkCreateResult { Id = "net-id" }));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var config = new NetworkCreateConfig { Name = "ignored" };

        await service.CreateNetworkAsync(
            "wins",
            config,
            TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("wins", captured.Name);
      }
      finally { kernel.Dispose(); }
    }
  }
}
