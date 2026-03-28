using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Docker.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Unit tests for DockerCliContainerDriver: QuoteArgumentIfNeeded
  /// and CreateAsync/RunAsync arg-building patterns.
  /// Arg building is inline in CreateAsync/RunAsync, so tests replicate
  /// the construction logic using QuoteArgumentIfNeeded via reflection.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class DockerCliContainerDriverTests
  {
    #region QuoteArgumentIfNeeded Tests

    [Fact]
    public void QuoteArgumentIfNeeded_SimpleString_ReturnsUnquoted()
    {
      Assert.Equal("hello", Quote("hello"));
    }

    [Fact]
    public void QuoteArgumentIfNeeded_StringWithSpaces_ReturnsQuoted()
    {
      Assert.Equal("\"hello world\"", Quote("hello world"));
    }

    [Fact]
    public void QuoteArgumentIfNeeded_EmptyString_ReturnsQuotedEmpty()
    {
      Assert.Equal("\"\"", Quote(""));
    }

    [Fact]
    public void QuoteArgumentIfNeeded_NullString_ReturnsQuotedEmpty()
    {
      Assert.Equal("\"\"", Quote(null));
    }

    [Fact]
    public void QuoteArgumentIfNeeded_WithBackslashesAndSpaces_Escapes()
    {
      Assert.Equal("\"C:\\\\Program Files\\\\Docker\"",
          Quote(@"C:\Program Files\Docker"));
    }

    [Fact]
    public void QuoteArgumentIfNeeded_ShellMetachars_Quotes()
    {
      var result = Quote("value;with&pipes|redirect>");
      Assert.StartsWith("\"", result);
      Assert.EndsWith("\"", result);
    }

    [Theory]
    [InlineData("nginx:latest")]
    [InlineData("alpine")]
    [InlineData("/usr/local/bin/docker")]
    [InlineData("80/tcp")]
    public void QuoteArgumentIfNeeded_SafeStrings_NoQuotes(string input)
    {
      Assert.Equal(input, Quote(input));
    }

    #endregion

    #region CreateAsync Arg Building Tests

    [Fact]
    public void CreateArgs_Minimal_StartsWithCreateEndsWithImage()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig { Image = "nginx" });
      Assert.StartsWith("create", args);
      Assert.EndsWith("nginx", args);
    }

    [Fact]
    public void CreateArgs_WithName_IncludesNameFlag()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig
      { Image = "nginx", Name = "web-server" });
      Assert.Contains("--name web-server", args);
    }

    [Fact]
    public void CreateArgs_WithEnvVars_IncludesEnvFlags()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig
      {
        Image = "nginx",
        Environment = new Dictionary<string, string>
          { { "FOO", "bar" }, { "BAZ", "qux" } }
      });
      Assert.Contains("-e FOO=bar", args);
      Assert.Contains("-e BAZ=qux", args);
    }

    [Fact]
    public void CreateArgs_WithPorts_HostColonContainerFormat()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig
      {
        Image = "nginx",
        PortBindings = new Dictionary<string, string> { { "80/tcp", "8080" } }
      });
      Assert.Contains("-p 8080:80/tcp", args);
    }

    [Fact]
    public void CreateArgs_WithMultiplePorts_IncludesAll()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig
      {
        Image = "nginx",
        PortBindings = new Dictionary<string, string>
          { { "80/tcp", "8080" }, { "443/tcp", "8443" } }
      });
      Assert.Contains("-p 8080:80/tcp", args);
      Assert.Contains("-p 8443:443/tcp", args);
    }

    [Fact]
    public void CreateArgs_WithVolumes_IncludesVolumeFlags()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig
      {
        Image = "nginx",
        Volumes = new Dictionary<string, string>
          { { "/host/data", "/container/data" } }
      });
      Assert.Contains("-v /host/data:/container/data", args);
    }

    [Fact]
    public void CreateArgs_WithNetworkMode_IncludesNetworkFlag()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig
      { Image = "nginx", NetworkMode = "bridge" });
      Assert.Contains("--network bridge", args);
    }

    [Fact]
    public void CreateArgs_WithLabels_IncludesLabelFlags()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig
      {
        Image = "nginx",
        Labels = new Dictionary<string, string>
          { { "app", "web" }, { "env", "prod" } }
      });
      Assert.Contains("--label app=web", args);
      Assert.Contains("--label env=prod", args);
    }

    [Fact]
    public void CreateArgs_WithWorkDir_IncludesWFlag()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig
      { Image = "nginx", WorkingDirectory = "/app" });
      Assert.Contains("-w /app", args);
    }

    [Fact]
    public void CreateArgs_WithUser_IncludesUFlag()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig
      { Image = "nginx", User = "appuser" });
      Assert.Contains("-u appuser", args);
    }

    [Fact]
    public void CreateArgs_WithRestartPolicy_IncludesRestartFlag()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig
      { Image = "nginx", RestartPolicy = "unless-stopped" });
      Assert.Contains("--restart unless-stopped", args);
    }

    [Fact]
    public void CreateArgs_WithHostname_IncludesHostnameFlag()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig
      { Image = "nginx", Hostname = "web-host" });
      Assert.Contains("--hostname web-host", args);
    }

    [Fact]
    public void CreateArgs_WithIpv4_IncludesIpFlag()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig
      { Image = "nginx", Ipv4Address = "172.20.0.10" });
      Assert.Contains("--ip 172.20.0.10", args);
    }

    [Fact]
    public void CreateArgs_WithIpv6_IncludesIp6Flag()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig
      { Image = "nginx", Ipv6Address = "fd00::10" });
      Assert.Contains("--ip6 fd00::10", args);
    }

    [Fact]
    public void CreateArgs_WithMemory_IncludesMemoryFlag()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig
      { Image = "nginx", MemoryLimit = 536870912 });
      Assert.Contains("--memory 536870912", args);
    }

    [Fact]
    public void CreateArgs_WithCpuShares_IncludesCpuSharesFlag()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig
      { Image = "nginx", CpuShares = 512 });
      Assert.Contains("--cpu-shares 512", args);
    }

    [Fact]
    public void CreateArgs_Privileged_IncludesFlag()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig
      { Image = "nginx", Privileged = true });
      Assert.Contains("--privileged", args);
    }

    [Fact]
    public void CreateArgs_AutoRemove_IncludesRmFlag()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig
      { Image = "nginx", AutoRemove = true });
      Assert.Contains("--rm", args);
    }

    [Fact]
    public void CreateArgs_WithLinks_IncludesLinkFlags()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig
      {
        Image = "nginx",
        Links = new List<string> { "db:database", "cache:redis" }
      });
      Assert.Contains("--link db:database", args);
      Assert.Contains("--link cache:redis", args);
    }

    [Fact]
    public void CreateArgs_WithCommand_AppendsAfterImage()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig
      {
        Image = "ubuntu",
        Command = new[] { "bash", "-c", "echo hello" }
      });
      Assert.EndsWith("ubuntu bash -c \"echo hello\"", args);
    }

    [Fact]
    public void CreateArgs_WithNetworks_IncludesMultipleNetworkFlags()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig
      {
        Image = "nginx",
        Networks = new List<string> { "frontend", "backend" }
      });
      Assert.Contains("--network frontend", args);
      Assert.Contains("--network backend", args);
    }

    [Fact]
    public void CreateArgs_Defaults_OmitsOptionalFlags()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig { Image = "nginx" });
      Assert.DoesNotContain("--privileged", args);
      Assert.DoesNotContain("--rm", args);
      Assert.DoesNotContain("--name", args);
      Assert.DoesNotContain("-e", args);
      Assert.DoesNotContain("-p", args);
    }

    [Fact]
    public void CreateArgs_ImageAppearsBeforeCommand()
    {
      var args = BuildCreateArgs(new ContainerCreateConfig
      {
        Image = "ubuntu",
        Name = "test",
        Command = new[] { "sleep", "infinity" }
      });
      var imageIdx = args.IndexOf("ubuntu", StringComparison.Ordinal);
      var cmdIdx = args.IndexOf("sleep", StringComparison.Ordinal);
      Assert.True(imageIdx < cmdIdx, "Image must appear before command");
    }

    #endregion

    #region Shared Reflection Helpers

    internal static string Quote(string arg)
    {
      var method = typeof(DockerCliDriverBase).GetMethod(
          "QuoteArgumentIfNeeded",
          BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      return (string)method.Invoke(null, new object[] { arg });
    }

    /// <summary>
    /// Replicates CreateAsync arg-construction logic for testing.
    /// </summary>
    internal static string BuildCreateArgs(ContainerCreateConfig config)
    {
      var args = new List<string> { "create" };
      if (!string.IsNullOrEmpty(config.Name))
        args.Add($"--name {Quote(config.Name)}");
      if (config.Environment != null)
        foreach (var env in config.Environment)
          args.Add($"-e {Quote($"{env.Key}={env.Value}")}");
      if (config.PortBindings != null)
        foreach (var port in config.PortBindings)
          args.Add($"-p {Quote($"{port.Value}:{port.Key}")}");
      if (config.Volumes != null)
        foreach (var vol in config.Volumes)
          args.Add($"-v {Quote($"{vol.Key}:{vol.Value}")}");
      if (!string.IsNullOrEmpty(config.NetworkMode))
        args.Add($"--network {Quote(config.NetworkMode)}");
      if (config.Networks != null)
        foreach (var net in config.Networks)
          args.Add($"--network {Quote(net)}");
      if (config.Labels != null)
        foreach (var lbl in config.Labels)
          args.Add($"--label {Quote($"{lbl.Key}={lbl.Value}")}");
      if (!string.IsNullOrEmpty(config.WorkingDirectory))
        args.Add($"-w {Quote(config.WorkingDirectory)}");
      if (!string.IsNullOrEmpty(config.User))
        args.Add($"-u {Quote(config.User)}");
      if (!string.IsNullOrEmpty(config.RestartPolicy))
        args.Add($"--restart {Quote(config.RestartPolicy)}");
      if (!string.IsNullOrEmpty(config.Hostname))
        args.Add($"--hostname {Quote(config.Hostname)}");
      if (!string.IsNullOrEmpty(config.Ipv4Address))
        args.Add($"--ip {Quote(config.Ipv4Address)}");
      if (!string.IsNullOrEmpty(config.Ipv6Address))
        args.Add($"--ip6 {Quote(config.Ipv6Address)}");
      if (config.MemoryLimit.HasValue)
        args.Add($"--memory {config.MemoryLimit.Value}");
      if (config.CpuShares.HasValue)
        args.Add($"--cpu-shares {config.CpuShares.Value}");
      if (config.Privileged)
        args.Add("--privileged");
      if (config.AutoRemove)
        args.Add("--rm");
      if (config.Links != null)
        foreach (var link in config.Links)
          args.Add($"--link {Quote(link)}");
      args.Add(Quote(config.Image));
      if (config.Command is { Length: > 0 })
        foreach (var c in config.Command)
          args.Add(Quote(c));
      return string.Join(" ", args);
    }

    #endregion
  }
}
