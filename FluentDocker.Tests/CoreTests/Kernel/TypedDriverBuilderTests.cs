using System;
using System.Reflection;
using FluentDocker.Kernel;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Kernel
{
  /// <summary>
  /// Unit tests for the type-safe driver builder interfaces and their internal implementations.
  /// Verifies that WithDockerCli(), WithDockerApi(), WithPodmanCli() on IKernelBuilder
  /// produce correct DriverConfiguration with the right driver packs and context settings.
  /// </summary>
  [Trait("Category", "Unit")]
  public class TypedDriverBuilderTests
  {
    #region DockerCliDriverBuilder

    [Fact]
    public void DockerCliBuilder_AsDefault_SetsIsDefault()
    {
      var result = BuildDockerCliConfig(b => b.AsDefault());
      Assert.True(result.IsDefault);
    }

    [Fact]
    public void DockerCliBuilder_AtHost_SetsHost()
    {
      var result = BuildDockerCliConfig(b => b
          .AtHost("tcp://remote:2376"));
      Assert.Equal("tcp://remote:2376", result.Context.Host);
    }

    [Fact]
    public void DockerCliBuilder_WithCertificates_SetsCertPath()
    {
      var result = BuildDockerCliConfig(b => b
          .WithCertificates("/certs/docker"));
      Assert.Equal("/certs/docker", result.Context.CertificatePath);
    }

    [Fact]
    public void DockerCliBuilder_WithSudo_NoPassword_SetsNoPassword()
    {
      var result = BuildDockerCliConfig(b => b
          .WithSudo(SudoMechanism.NoPassword));
      Assert.Equal(SudoMechanism.NoPassword, result.Context.Sudo);
      Assert.Null(result.Context.SudoPassword);
    }

    [Fact]
    public void DockerCliBuilder_WithSudo_Password_SetsBoth()
    {
      var result = BuildDockerCliConfig(b => b
          .WithSudo(SudoMechanism.Password, "secret123"));
      Assert.Equal(SudoMechanism.Password, result.Context.Sudo);
      Assert.Equal("secret123", result.Context.SudoPassword);
    }

    [Fact]
    public void DockerCliBuilder_ChainsCorrectly()
    {
      var result = BuildDockerCliConfig(b => b
          .AtHost("tcp://host:2376")
          .WithCertificates("/certs")
          .WithSudo(SudoMechanism.NoPassword)
          .AsDefault());

      Assert.True(result.IsDefault);
      Assert.Equal("tcp://host:2376", result.Context.Host);
      Assert.Equal("/certs", result.Context.CertificatePath);
      Assert.Equal(SudoMechanism.NoPassword, result.Context.Sudo);
    }

    [Fact]
    public void DockerCliBuilder_CreatesDockerCliDriverPack()
    {
      var result = BuildDockerCliConfig(b => b.AsDefault());
      Assert.Equal(
          "FluentDocker.Drivers.Docker.Cli.DockerCliDriverPack",
          result.DriverPackTypeName);
    }

    [Fact]
    public void DockerCliBuilder_SetsDriverId()
    {
      var result = BuildDockerCliConfig(b => b.AsDefault(), "my-docker");
      Assert.Equal("my-docker", result.DriverId);
    }

    #endregion

    #region DockerApiDriverBuilder

    [Fact]
    public void DockerApiBuilder_AsDefault_SetsIsDefault()
    {
      var result = BuildDockerApiConfig(b => b.AsDefault());
      Assert.True(result.IsDefault);
    }

    [Fact]
    public void DockerApiBuilder_AtHost_SetsHost()
    {
      var result = BuildDockerApiConfig(b => b
          .AtHost("tcp://api-host:2376"));
      Assert.Equal("tcp://api-host:2376", result.Context.Host);
    }

    [Fact]
    public void DockerApiBuilder_WithConnectionTimeout_SetsInContext()
    {
      var result = BuildDockerApiConfig(b => b
          .WithConnectionTimeout(TimeSpan.FromSeconds(15)));
      Assert.Equal(TimeSpan.FromSeconds(15), result.Context.ConnectionTimeout);
    }

    [Fact]
    public void DockerApiBuilder_WithRequestTimeout_SetsInContext()
    {
      var result = BuildDockerApiConfig(b => b
          .WithRequestTimeout(TimeSpan.FromMinutes(10)));
      Assert.Equal(TimeSpan.FromMinutes(10), result.Context.RequestTimeout);
    }

    [Fact]
    public void DockerApiBuilder_WithApiVersion_SetsInContext()
    {
      var result = BuildDockerApiConfig(b => b
          .WithApiVersion("1.41"));
      Assert.Equal("1.41", result.Context.ApiVersion);
    }

    [Fact]
    public void DockerApiBuilder_WithTlsVerification_False_SetsInContext()
    {
      var result = BuildDockerApiConfig(b => b
          .WithTlsVerification(false));
      Assert.False(result.Context.VerifyTls);
    }

    [Fact]
    public void DockerApiBuilder_WithTlsVerification_True_SetsInContext()
    {
      var result = BuildDockerApiConfig(b => b
          .WithTlsVerification());
      Assert.True(result.Context.VerifyTls);
    }

    [Fact]
    public void DockerApiBuilder_ChainsCorrectly()
    {
      var result = BuildDockerApiConfig(b => b
          .AtHost("tcp://host:2376")
          .WithCertificates("/certs")
          .WithConnectionTimeout(TimeSpan.FromSeconds(20))
          .WithRequestTimeout(TimeSpan.FromMinutes(3))
          .WithApiVersion("1.43")
          .WithTlsVerification(false)
          .AsDefault());

      Assert.True(result.IsDefault);
      Assert.Equal("tcp://host:2376", result.Context.Host);
      Assert.Equal("/certs", result.Context.CertificatePath);
      Assert.Equal(TimeSpan.FromSeconds(20), result.Context.ConnectionTimeout);
      Assert.Equal(TimeSpan.FromMinutes(3), result.Context.RequestTimeout);
      Assert.Equal("1.43", result.Context.ApiVersion);
      Assert.False(result.Context.VerifyTls);
    }

    [Fact]
    public void DockerApiBuilder_CreatesDockerApiDriverPack()
    {
      var result = BuildDockerApiConfig(b => b.AsDefault());
      Assert.Equal(
          "FluentDocker.Drivers.Docker.Api.DockerApiDriverPack",
          result.DriverPackTypeName);
    }

    [Fact]
    public void DockerApiBuilder_DefaultTimeouts_AreNull()
    {
      var result = BuildDockerApiConfig(b => b.AsDefault());
      Assert.Null(result.Context.ConnectionTimeout);
      Assert.Null(result.Context.RequestTimeout);
      Assert.Null(result.Context.ApiVersion);
    }

    #endregion

    #region PodmanCliDriverBuilder

    [Fact]
    public void PodmanCliBuilder_AsDefault_SetsIsDefault()
    {
      var result = BuildPodmanCliConfig(b => b.AsDefault());
      Assert.True(result.IsDefault);
    }

    [Fact]
    public void PodmanCliBuilder_AtHost_SetsHost()
    {
      var result = BuildPodmanCliConfig(b => b
          .AtHost("unix:///run/podman/podman.sock"));
      Assert.Equal("unix:///run/podman/podman.sock", result.Context.Host);
    }

    [Fact]
    public void PodmanCliBuilder_WithAutoStartMachine_NoAction_SetsDefault()
    {
      var result = BuildPodmanCliConfig(b => b
          .WithAutoStartMachine());
      Assert.NotNull(result.Context.AutoStartMachine);
      Assert.Null(result.Context.AutoStartMachine.MachineName);
      Assert.False(result.Context.AutoStartMachine.CreateIfNotExists);
    }

    [Fact]
    public void PodmanCliBuilder_WithAutoStartMachine_WithConfigure_SetsProperties()
    {
      var result = BuildPodmanCliConfig(b => b
          .WithAutoStartMachine(c =>
          {
            c.MachineName = "dev-machine";
            c.CreateIfNotExists = true;
            c.InitCpus = 4;
            c.InitMemoryMiB = 8192;
          }));
      Assert.NotNull(result.Context.AutoStartMachine);
      Assert.Equal("dev-machine", result.Context.AutoStartMachine.MachineName);
      Assert.True(result.Context.AutoStartMachine.CreateIfNotExists);
      Assert.Equal(4, result.Context.AutoStartMachine.InitCpus);
      Assert.Equal(8192, result.Context.AutoStartMachine.InitMemoryMiB);
    }

    [Fact]
    public void PodmanCliBuilder_WithSudo_SetsInContext()
    {
      var result = BuildPodmanCliConfig(b => b
          .WithSudo(SudoMechanism.NoPassword));
      Assert.Equal(SudoMechanism.NoPassword, result.Context.Sudo);
    }

    [Fact]
    public void PodmanCliBuilder_ChainsCorrectly()
    {
      var result = BuildPodmanCliConfig(b => b
          .AtHost("unix:///run/podman/podman.sock")
          .WithCertificates("/certs")
          .WithAutoStartMachine(c => c.MachineName = "test")
          .WithSudo(SudoMechanism.NoPassword)
          .AsDefault());

      Assert.True(result.IsDefault);
      Assert.Equal("unix:///run/podman/podman.sock", result.Context.Host);
      Assert.Equal("/certs", result.Context.CertificatePath);
      Assert.NotNull(result.Context.AutoStartMachine);
      Assert.Equal("test", result.Context.AutoStartMachine.MachineName);
      Assert.Equal(SudoMechanism.NoPassword, result.Context.Sudo);
    }

    [Fact]
    public void PodmanCliBuilder_CreatesPodmanCliDriverPack()
    {
      var result = BuildPodmanCliConfig(b => b.AsDefault());
      Assert.Equal(
          "FluentDocker.Drivers.Podman.Cli.PodmanCliDriverPack",
          result.DriverPackTypeName);
    }

    #endregion

    #region KernelBuilder Typed Methods

    [Fact]
    public void WithDockerCli_NullId_Throws()
    {
      Assert.Throws<ArgumentException>(() =>
          FluentDockerKernel.Create()
              .WithDockerCli(null!, d => d.AsDefault()));
    }

    [Fact]
    public void WithDockerCli_EmptyId_Throws()
    {
      Assert.Throws<ArgumentException>(() =>
          FluentDockerKernel.Create()
              .WithDockerCli("", d => d.AsDefault()));
    }

    [Fact]
    public void WithDockerCli_NullConfigure_Throws()
    {
      Assert.Throws<ArgumentNullException>(() =>
          FluentDockerKernel.Create()
              .WithDockerCli("docker", null!));
    }

    [Fact]
    public void WithDockerApi_NullConfigure_Throws()
    {
      Assert.Throws<ArgumentNullException>(() =>
          FluentDockerKernel.Create()
              .WithDockerApi("api", null!));
    }

    [Fact]
    public void WithPodmanCli_NullConfigure_Throws()
    {
      Assert.Throws<ArgumentNullException>(() =>
          FluentDockerKernel.Create()
              .WithPodmanCli("podman", null!));
    }

    [Fact]
    public void WithDriver_CustomPack_NullConfigure_Throws()
    {
      Assert.Throws<ArgumentNullException>(() =>
          FluentDockerKernel.Create()
              .WithDriver("custom", null!));
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Uses reflection to create and build the internal DockerCliDriverBuilder.
    /// </summary>
    private static DriverConfigResult BuildDockerCliConfig(
        Action<IDockerCliDriverBuilder> configure, string driverId = "docker")
    {
      return BuildTypedDriverConfig("DockerCliDriverBuilder", driverId, configure);
    }

    /// <summary>
    /// Uses reflection to create and build the internal DockerApiDriverBuilder.
    /// </summary>
    private static DriverConfigResult BuildDockerApiConfig(
        Action<IDockerApiDriverBuilder> configure, string driverId = "api")
    {
      return BuildTypedDriverConfig("DockerApiDriverBuilder", driverId, configure);
    }

    /// <summary>
    /// Uses reflection to create and build the internal PodmanCliDriverBuilder.
    /// </summary>
    private static DriverConfigResult BuildPodmanCliConfig(
        Action<IPodmanCliDriverBuilder> configure, string driverId = "podman")
    {
      return BuildTypedDriverConfig("PodmanCliDriverBuilder", driverId, configure);
    }

    private static DriverConfigResult BuildTypedDriverConfig<T>(
        string typeName, string driverId, Action<T> configure)
    {
      var builderType = typeof(KernelBuilder).Assembly
          .GetType($"FluentDocker.Kernel.{typeName}");
      Assert.NotNull(builderType);

      var instance = Activator.CreateInstance(builderType, driverId);
      Assert.NotNull(instance);

      configure((T)instance);

      var buildMethod = builderType.GetMethod("Build",
          BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.NotNull(buildMethod);

      var configObj = buildMethod.Invoke(instance, null);
      Assert.NotNull(configObj);

      var contextProp = configObj.GetType().GetProperty("Context");
      var isDefaultProp = configObj.GetType().GetProperty("IsDefault");
      var driverIdProp = configObj.GetType().GetProperty("DriverId");
      var driverPackProp = configObj.GetType().GetProperty("DriverPack");

      return new DriverConfigResult
      {
        Context = (DriverContext)contextProp?.GetValue(configObj),
        IsDefault = (bool)(isDefaultProp?.GetValue(configObj) ?? false),
        DriverId = (string)driverIdProp?.GetValue(configObj),
        DriverPackTypeName = driverPackProp?.GetValue(configObj)?.GetType().FullName,
      };
    }

    private class DriverConfigResult
    {
      public required DriverContext Context { get; set; }
      public bool IsDefault { get; set; }
      public required string DriverId { get; set; }
      public required string DriverPackTypeName { get; set; }
    }

    #endregion
  }
}
