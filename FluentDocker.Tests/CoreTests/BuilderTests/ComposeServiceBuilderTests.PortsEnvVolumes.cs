using System;
using FluentDocker.Common;
using FluentDocker.Model.Compose;
using FluentDocker.Model.Containers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// ComposeServiceBuilder tests for Ports, Environment, Volumes, and fluent chaining.
  /// </summary>
  public partial class ComposeServiceBuilderTests
  {
    #region Ports (long form)

    [Fact]
    public void Ports_LongForm_AddsPortDefinition()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      var result = builder.Ports(80, 8080);

      // Assert
      var config = GetConfig(builder);
      Assert.Single(config.Ports);
      var port = Assert.IsType<PortsLongDefinition>(config.Ports[0]);
      Assert.Equal(80, port.Target);
      Assert.Equal(8080, port.Published);
      Assert.Equal(PortMode.Host, port.Mode);
      Assert.Equal("tcp", port.Protocol);
      Assert.Same(builder, result);
    }

    [Fact]
    public void Ports_LongForm_WithUdpProtocol()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      builder.Ports(53, 5353, protocol: "udp");

      // Assert
      var port = Assert.IsType<PortsLongDefinition>(GetConfig(builder).Ports[0]);
      Assert.Equal("udp", port.Protocol);
    }

    [Fact]
    public void Ports_LongForm_WithIngressMode()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      builder.Ports(80, 8080, PortMode.Ingress);

      // Assert
      var port = Assert.IsType<PortsLongDefinition>(GetConfig(builder).Ports[0]);
      Assert.Equal(PortMode.Ingress, port.Mode);
    }

    [Fact]
    public void Ports_LongForm_MultipleAddsAccumulate()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      builder
        .Ports(80, 8080)
        .Ports(443, 8443, protocol: "tcp");

      // Assert
      Assert.Equal(2, GetConfig(builder).Ports.Count);
    }

    #endregion

    #region Ports (short form)

    [Fact]
    public void Ports_ShortForm_AddsStringEntries()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      var result = builder.Ports("8080:80", "9090:90");

      // Assert
      var config = GetConfig(builder);
      Assert.Equal(2, config.Ports.Count);
      var first = Assert.IsType<PortsShortDefinition>(config.Ports[0]);
      var second = Assert.IsType<PortsShortDefinition>(config.Ports[1]);
      Assert.Equal("8080:80", first.Entry);
      Assert.Equal("9090:90", second.Entry);
      Assert.Same(builder, result);
    }

    [Fact]
    public void Ports_ShortForm_NullArray_DoesNothing()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      var result = builder.Ports(ports: null);

      // Assert
      Assert.Empty(GetConfig(builder).Ports);
      Assert.Same(builder, result);
    }

    [Fact]
    public void Ports_ShortForm_EmptyArray_DoesNothing()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      var result = builder.Ports([]);

      // Assert
      Assert.Empty(GetConfig(builder).Ports);
      Assert.Same(builder, result);
    }

    [Theory]
    [InlineData("3000")]
    [InlineData("8000:8000")]
    [InlineData("127.0.0.1:8001:8001")]
    [InlineData("6060:6060/udp")]
    public void Ports_ShortForm_VariousFormats(string portSpec)
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      builder.Ports(portSpec);

      // Assert
      var port = Assert.IsType<PortsShortDefinition>(GetConfig(builder).Ports[0]);
      Assert.Equal(portSpec, port.Entry);
    }

    #endregion

    #region Environment

    [Fact]
    public void Environment_EqualsFormat_ParsesNameAndValue()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      var result = builder.Environment("DB_HOST=localhost");

      // Assert
      var env = GetConfig(builder).Environment;
      Assert.Single(env);
      Assert.Equal("localhost", env["DB_HOST"]);
      Assert.Same(builder, result);
    }

    [Fact]
    public void Environment_MultipleEqualsFormat_ParsesAll()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      builder.Environment("DB_HOST=localhost", "DB_PORT=5432", "DB_NAME=test");

      // Assert
      var env = GetConfig(builder).Environment;
      Assert.Equal(3, env.Count);
      Assert.Equal("localhost", env["DB_HOST"]);
      Assert.Equal("5432", env["DB_PORT"]);
      Assert.Equal("test", env["DB_NAME"]);
    }

    [Fact]
    public void Environment_NameValuePairs_ParsesAlternating()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act - alternating name, value pairs (no = sign)
      builder.Environment("DB_HOST", "localhost", "DB_PORT", "5432");

      // Assert
      var env = GetConfig(builder).Environment;
      Assert.Equal(2, env.Count);
      Assert.Equal("localhost", env["DB_HOST"]);
      Assert.Equal("5432", env["DB_PORT"]);
    }

    [Fact]
    public void Environment_NullArray_DoesNothing()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      var result = builder.Environment(nameAndValue: null);

      // Assert
      Assert.Empty(GetConfig(builder).Environment);
      Assert.Same(builder, result);
    }

    [Fact]
    public void Environment_EmptyArray_DoesNothing()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      var result = builder.Environment([]);

      // Assert
      Assert.Empty(GetConfig(builder).Environment);
      Assert.Same(builder, result);
    }

    [Fact]
    public void Environment_MixedFormats_ThrowsException()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act & Assert - mixing name (no =) followed by name=value throws
      Assert.Throws<FluentDockerException>(() =>
        builder.Environment("ORPHAN_NAME", "DB_HOST=localhost"));
    }

    [Fact]
    public void Environment_ValueContainsEquals_ParsesCorrectly()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act - value itself contains an equals sign
      builder.Environment("CONNECTION=Server=db;Port=5432");

      // Assert
      var env = GetConfig(builder).Environment;
      Assert.Equal("Server=db;Port=5432", env["CONNECTION"]);
    }

    [Fact]
    public void Environment_MultipleCallsAccumulate()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      builder
        .Environment("KEY1=val1")
        .Environment("KEY2=val2");

      // Assert
      var env = GetConfig(builder).Environment;
      Assert.Equal(2, env.Count);
      Assert.Equal("val1", env["KEY1"]);
      Assert.Equal("val2", env["KEY2"]);
    }

    #endregion

    #region Volume (short form)

    [Fact]
    public void Volume_ShortForm_CreatesShortDefinition()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      var result = builder.Volume("/data/db", "/host/data");

      // Assert
      var config = GetConfig(builder);
      Assert.Single(config.Volumes);
      var vol = Assert.IsType<ShortServiceVolumeDefinition>(config.Volumes[0]);
      Assert.Contains("/host/data", vol.Entry);
      Assert.Contains("/data/db", vol.Entry);
      Assert.Same(builder, result);
    }

    [Fact]
    public void Volume_ShortForm_EntryFormat_IsHostColonContainer()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      builder.Volume("/container/path", "/host/path");

      // Assert
      var vol = Assert.IsType<ShortServiceVolumeDefinition>(
        GetConfig(builder).Volumes[0]);
      Assert.Equal("/host/path:/container/path", vol.Entry);
    }

    #endregion

    #region Volume (long form)

    [Fact]
    public void Volume_LongForm_CreatesLongDefinition()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      var result = builder.Volume("/container/data", "/host/data",
        isReadonly: true);

      // Assert
      var config = GetConfig(builder);
      Assert.Single(config.Volumes);
      var vol = Assert.IsType<LongServiceVolumeDefinition>(config.Volumes[0]);
      Assert.Equal("/host/data", vol.Source);
      Assert.Equal("/container/data", vol.Target);
      Assert.True(vol.IsReadOnly);
      Assert.Same(builder, result);
    }

    [Fact]
    public void Volume_LongForm_DefaultsToReadWrite()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      builder.Volume("/container", "/host", isReadonly: false);

      // Assert
      var vol = Assert.IsType<LongServiceVolumeDefinition>(
        GetConfig(builder).Volumes[0]);
      Assert.False(vol.IsReadOnly);
    }

    [Fact]
    public void Volume_MultipleCallsAccumulate()
    {
      // Arrange
      var builder = CreateBuilder();

      // Act
      builder
        .Volume("/data", "/host/data")
        .Volume("/logs", "/host/logs");

      // Assert
      Assert.Equal(2, GetConfig(builder).Volumes.Count);
    }

    #endregion

    #region Fluent chaining

    [Fact]
    public void FluentChaining_AllMethodsReturnSelf()
    {
      // Arrange
      var builder = CreateBuilder("webapp");

      // Act - chain all methods together
      var result = builder
        .Image("nginx:latest")
        .Restart(RestartPolicy.Always)
        .Volume("/var/log", "/host/logs")
        .Volume("/data", "/host/data", isReadonly: true)
        .Environment("ENV=production", "PORT=8080")
        .DependsOn("redis", "postgres")
        .Ports(80, 8080)
        .Ports("443:443");

      // Assert
      Assert.Same(builder, result);
    }

    [Fact]
    public void FluentChaining_AllConfigValuesSet()
    {
      // Arrange & Act
      var builder = CreateBuilder("webapp")
        .Image("nginx:latest")
        .Restart(RestartPolicy.Always)
        .Environment("ENV=production")
        .DependsOn("redis")
        .Ports(80, 8080);

      // Assert
      var config = GetConfig(builder);
      Assert.Equal("webapp", config.Name);
      Assert.Equal("nginx:latest", config.Image);
      Assert.Equal(RestartPolicy.Always, config.Restart);
      Assert.Equal("production", config.Environment["ENV"]);
      Assert.Contains("redis", config.DependsOn);
      Assert.Single(config.Ports);
    }

    [Fact]
    public void FluentChaining_MixedPortFormats()
    {
      // Arrange & Act
      var builder = CreateBuilder("web")
        .Ports(80, 8080, PortMode.Host, "tcp")
        .Ports("9090:90", "3000");

      // Assert
      var ports = GetConfig(builder).Ports;
      Assert.Equal(3, ports.Count);
      Assert.IsType<PortsLongDefinition>(ports[0]);
      Assert.IsType<PortsShortDefinition>(ports[1]);
      Assert.IsType<PortsShortDefinition>(ports[2]);
    }

    [Fact]
    public void FluentChaining_MixedVolumeFormats()
    {
      // Arrange & Act
      var builder = CreateBuilder("db")
        .Volume("/data", "/host/data")
        .Volume("/logs", "/host/logs", isReadonly: true);

      // Assert
      var volumes = GetConfig(builder).Volumes;
      Assert.Equal(2, volumes.Count);
      Assert.IsType<ShortServiceVolumeDefinition>(volumes[0]);
      Assert.IsType<LongServiceVolumeDefinition>(volumes[1]);
    }

    #endregion
  }
}
