using System;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public class Docker29CliParsingTests
  {
    [Fact]
    public async Task NetworkList_ParsesDocker29StringBooleans()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var driver = CreateNetworkDriver("""
          #!/bin/sh
          cat <<'JSON'
          {"CreatedAt":"2026-07-03 00:03:17.94287775 +0000 UTC","Driver":"bridge","ID":"150ce2176080","IPv4":"true","IPv6":"false","Internal":"false","Labels":"","Name":"bridge","Scope":"local"}
          JSON
          """);

      var response = await driver.ListAsync(
          new DriverContext("docker"), cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      var network = Assert.Single(response.Data);
      Assert.Equal("bridge", network.Name);
      Assert.False(network.IPv6);
      Assert.False(network.Internal);
    }

    [Fact]
    public async Task NetworkList_AllLinesFailingParse_ReturnsFailure()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var driver = CreateNetworkDriver("#!/bin/sh\necho '{not json}'\n");

      var response = await driver.ListAsync(
          new DriverContext("docker"), cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.Contains("parse", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ServiceList_ParsesDocker29EmptyPortsString()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var driver = CreateServiceDriver("""
          #!/bin/sh
          cat <<'JSON'
          {"ID":"5i6l1do7kpv7","Image":"nginx:alpine","Mode":"replicated","Name":"svc","Ports":"","Replicas":"1/1"}
          JSON
          """);

      var response = await driver.ListAsync(
          new DriverContext("docker"), cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      var service = Assert.Single(response.Data);
      Assert.Equal("svc", service.Name);
      Assert.Empty(service.Ports);
    }

    [Fact]
    public async Task ServiceList_AllLinesFailingParse_ReturnsFailure()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var driver = CreateServiceDriver("#!/bin/sh\necho '{not json}'\n");

      var response = await driver.ListAsync(
          new DriverContext("docker"), cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.Contains("parse", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StackList_AllLinesFailingParse_ReturnsFailure()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var driver = CreateStackDriver("#!/bin/sh\necho '{not json}'\n");

      var response = await driver.ListAsync(
          new DriverContext("docker"), cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.Contains("parse", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StackServices_AllLinesFailingParse_ReturnsFailure()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var driver = CreateStackDriver("#!/bin/sh\necho '{not json}'\n");

      var response = await driver.GetServicesAsync(
          new DriverContext("docker"), "stack", cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.Contains("parse", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VolumeList_ParsesDocker29StringLabels()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var driver = CreateVolumeDriver("""
          #!/bin/sh
          cat <<'JSON'
          {"Availability":"N/A","Driver":"local","Group":"N/A","Labels":"com.docker.volume.anonymous=","Links":"N/A","Mountpoint":"/var/lib/docker/volumes/v/_data","Name":"v","Scope":"local","Size":"N/A","Status":"N/A"}
          JSON
          """);

      var response = await driver.ListAsync(
          new DriverContext("docker"), cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      var volume = Assert.Single(response.Data);
      Assert.Equal("v", volume.Name);
      Assert.True(volume.Labels.ContainsKey("com.docker.volume.anonymous"));
    }

    [Fact]
    public async Task VolumeList_AllLinesFailingParse_ReturnsFailure()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var driver = CreateVolumeDriver("#!/bin/sh\necho '{not json}'\n");

      var response = await driver.ListAsync(
          new DriverContext("docker"), cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.Contains("parse", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetInfo_ParsesDocker29ServerVersion()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var driver = CreateSystemDriver("""
          #!/bin/sh
          cat <<'JSON'
          {"Containers":1,"ContainersRunning":1,"Driver":"overlayfs","LoggingDriver":"json-file","Name":"docker-desktop","NCPU":14,"MemTotal":8320954368,"OperatingSystem":"Docker Desktop","OSType":"linux","Architecture":"aarch64","DockerRootDir":"/var/lib/docker","ServerVersion":"29.5.3","Runtimes":{},"Swarm":{"LocalNodeState":"active"}}
          JSON
          """);

      var response = await driver.GetInfoAsync(
          new DriverContext("docker"), TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      Assert.Equal("29.5.3", response.Data.EngineVersion);
      Assert.Equal("29.5.3", response.Data.ServerVersion);
    }

    private static DockerCliNetworkDriver CreateNetworkDriver(string script)
    {
      var driver = new DockerCliNetworkDriver(CreateResolver("docker-network", script));
      driver.Initialize(new DriverContext("docker"));
      return driver;
    }

    private static DockerCliServiceDriver CreateServiceDriver(string script)
    {
      var driver = new DockerCliServiceDriver(CreateResolver("docker-service", script));
      driver.Initialize(new DriverContext("docker"));
      return driver;
    }

    private static DockerCliStackDriver CreateStackDriver(string script)
    {
      var driver = new DockerCliStackDriver(CreateResolver("docker-stack", script));
      driver.Initialize(new DriverContext("docker"));
      return driver;
    }

    private static DockerCliVolumeDriver CreateVolumeDriver(string script)
    {
      var driver = new DockerCliVolumeDriver(CreateResolver("docker-volume", script));
      driver.Initialize(new DriverContext("docker"));
      return driver;
    }

    private static DockerCliSystemDriver CreateSystemDriver(string script)
    {
      var driver = new DockerCliSystemDriver(CreateResolver("docker-info", script));
      driver.Initialize(new DriverContext("docker"));
      return driver;
    }

    private static IBinaryResolver CreateResolver(string name, string script)
    {
      var dir = Path.Combine(AppContext.BaseDirectory, ".out", name, Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      WriteExecutable(Path.Combine(dir, "docker"), script);
      return new DockerResolver(dir);
    }

    private static void WriteExecutable(string path, string script)
    {
      File.WriteAllText(path, script.Replace("\r\n", "\n"));
      if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private sealed class DockerResolver(string directory) : IBinaryResolver
    {
      private readonly DockerBinary _binary = new(directory, "docker", SudoMechanism.None, null!);
      public DockerBinary[] Binaries => [_binary];
      public DockerBinary MainDockerClient => _binary;
      public DockerBinary MainDockerCompose => _binary;
      public DockerBinary MainDockerCli => _binary;
      public DockerBinary Resolve(string binary) => _binary;
      public string ResolveBinaryPath(string dockerCommand) => _binary.FqPath;
    }
  }
}
