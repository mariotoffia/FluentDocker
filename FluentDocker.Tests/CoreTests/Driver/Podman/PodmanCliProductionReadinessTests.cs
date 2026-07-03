using System;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  [Trait("Category", "Unit")]
  public class PodmanCliProductionReadinessTests
  {
    [Fact]
    public async Task ListPodsAsync_ParsesPodman6ContainerNamesStatusAndCountsCapturedFixture()
    {
      RequirePosixShellFixture();

      var driver = CreatePodDriver(ReturnJson(PodPsJson));

      var result = await driver.ListPodsAsync(
          new DriverContext("podman"),
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var pod = Assert.Single(result.Data);
      Assert.Equal("fd-wp5-20260703173403-pod", pod.Name);
      Assert.Equal(2, pod.NumContainers);
      Assert.Equal("8a271f4b082c-infra", pod.Containers[0].Name);
      Assert.Equal("running", pod.Containers[0].State);
      Assert.Equal("fd-wp5-20260703173403-ctr", pod.Containers[1].Name);
      Assert.Equal("running", pod.Containers[1].State);
    }

    [Fact]
    public async Task InspectPodAsync_ParsesPodman6InfraContainerID()
    {
      RequirePosixShellFixture();

      var driver = CreatePodDriver(ReturnJson(PodInspectJson));

      var result = await driver.InspectPodAsync(
          new DriverContext("podman"),
          "fd-wp5-20260703173403-pod",
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal("d9017a5a328eb7fac74e73b53bbdf5fcbdefb6ccf06fdf32f61e2760dc309c01", result.Data.InfraContainerId);
      Assert.Equal(2, result.Data.NumContainers);
    }

    [Fact]
    public async Task InspectEmptyArrays_ReturnNotFound()
    {
      RequirePosixShellFixture();

      var script = ReturnJson("[]");

      var container = CreateContainerDriver(script);
      var containerResult = await container.InspectAsync(
          new DriverContext("podman"), "missing", TestContext.Current.CancellationToken);
      Assert.False(containerResult.Success);
      Assert.Equal(ErrorCodes.Container.NotFound, containerResult.ErrorCode);

      var image = CreateImageDriver(script);
      var imageResult = await image.InspectAsync(
          new DriverContext("podman"), "missing", TestContext.Current.CancellationToken);
      Assert.False(imageResult.Success);
      Assert.Equal(ErrorCodes.Image.NotFound, imageResult.ErrorCode);

      var pod = CreatePodDriver(script);
      var podResult = await pod.InspectPodAsync(
          new DriverContext("podman"), "missing", TestContext.Current.CancellationToken);
      Assert.False(podResult.Success);
      Assert.Equal(ErrorCodes.Pod.NotFound, podResult.ErrorCode);

      var network = CreateNetworkDriver(script);
      var networkResult = await network.InspectAsync(
          new DriverContext("podman"), "missing", TestContext.Current.CancellationToken);
      Assert.False(networkResult.Success);
      Assert.Equal(ErrorCodes.Network.NotFound, networkResult.ErrorCode);

      var volume = CreateVolumeDriver(script);
      var volumeResult = await volume.InspectAsync(
          new DriverContext("podman"), "missing", TestContext.Current.CancellationToken);
      Assert.False(volumeResult.Success);
      Assert.Equal(ErrorCodes.Volume.NotFound, volumeResult.ErrorCode);
    }

    [Fact]
    public async Task InspectPodAsync_FallsBackToContainerCountWhenNumContainersMissing()
    {
      RequirePosixShellFixture();

      var driver = CreatePodDriver(ReturnJson(PodInspectJson.Replace("\"NumContainers\": 2,", "")));

      var result = await driver.InspectPodAsync(
          new DriverContext("podman"), "fd-wp5-20260703173403-pod",
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(2, result.Data.NumContainers);
    }

    [Fact]
    public async Task TopAsync_PsOptionsAreQuotedAndNotShellExecuted()
    {
      RequirePosixShellFixture();

      var dir = CreateOutputDirectory("podman-top-quote");
      var record = Path.Combine(dir, "args.txt");
      var marker = Path.Combine(dir, "injected");
      WriteExecutable(Path.Combine(dir, "podman"), $"""
          #!/bin/sh
          printf '%s\n' "$@" > '{record}'
          exit 0
          """);
      var driver = new PodmanCliContainerDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));

      var result = await driver.TopAsync(
          new DriverContext("podman"),
          "ctr",
          $"aux; touch {marker}",
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.False(File.Exists(marker));
      Assert.Equal(["top", "ctr", "aux;", "touch", marker], await ReadArgsAsync(record));
    }

    [Fact]
    public async Task LeadingDashPodNameFailsBeforeProcessStarts()
    {
      RequirePosixShellFixture();

      var dir = CreateOutputDirectory("podman-leading-dash");
      var marker = Path.Combine(dir, "invoked");
      WriteExecutable(Path.Combine(dir, "podman"), $"""
          #!/bin/sh
          touch '{marker}'
          exit 0
          """);
      var driver = new PodmanCliPodDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));

      var result = await driver.RemovePodAsync(
          new DriverContext("podman"), "--all", cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task SpaceContainingPodNameIsSinglePositionalArgument()
    {
      RequirePosixShellFixture();

      var dir = CreateOutputDirectory("podman-pod-space");
      var record = Path.Combine(dir, "args.txt");
      WriteExecutable(Path.Combine(dir, "podman"), $"""
          #!/bin/sh
          printf '%s\n' "$@" > '{record}'
          exit 0
          """);
      var driver = new PodmanCliPodDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));

      var result = await driver.StartPodAsync(
          new DriverContext("podman"), "my pod", TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(["pod", "start", "my pod"], await ReadArgsAsync(record));
    }

    [Fact]
    public async Task PerCallContext_ReachesPodmanDrivers()
    {
      RequirePosixShellFixture();

      var dir = CreateOutputDirectory("podman-context");
      var record = Path.Combine(dir, "args.txt");
      WriteExecutable(Path.Combine(dir, "podman"), FakePodmanForContext(record));
      var componentContext = new DriverContext("podman") { Host = "tcp://component:1234" };
      var perCallContext = new DriverContext("podman") { Host = "tcp://per-call:5678" };

      var image = CreateImageDriver(dir, componentContext);
      Assert.True((await image.PullAsync(perCallContext, "alpine", "latest", cancellationToken: TestContext.Current.CancellationToken)).Success);
      await AssertPerCallHostAsync(record);

      var network = new PodmanCliNetworkDriver(new PodmanResolver(dir));
      network.Initialize(componentContext);
      Assert.True((await network.RemoveAsync(perCallContext, "net", TestContext.Current.CancellationToken)).Success);
      await AssertPerCallHostAsync(record);

      var pod = new PodmanCliPodDriver(new PodmanResolver(dir));
      pod.Initialize(componentContext);
      Assert.True((await pod.ListPodsAsync(perCallContext, TestContext.Current.CancellationToken)).Success);
      await AssertPerCallHostAsync(record);

      var volume = new PodmanCliVolumeDriver(new PodmanResolver(dir));
      volume.Initialize(componentContext);
      Assert.True((await volume.InspectAsync(perCallContext, "vol", TestContext.Current.CancellationToken)).Success);
      await AssertPerCallHostAsync(record);

      var machine = new PodmanCliMachineDriver(new PodmanResolver(dir));
      machine.Initialize(componentContext);
      Assert.True((await machine.ListAsync(perCallContext, TestContext.Current.CancellationToken)).Success);
      await AssertPerCallHostAsync(record);

      var kube = new PodmanCliKubernetesDriver(new PodmanResolver(dir));
      kube.Initialize(componentContext);
      Assert.True((await kube.DownAsync(perCallContext, "pod.yaml", TestContext.Current.CancellationToken)).Success);
      await AssertPerCallHostAsync(record);

      var manifest = new PodmanCliManifestDriver(new PodmanResolver(dir));
      manifest.Initialize(componentContext);
      Assert.True((await manifest.RemoveAsync(perCallContext, "list", TestContext.Current.CancellationToken)).Success);
      await AssertPerCallHostAsync(record);

      var stream = new PodmanCliStreamDriver(new PodmanResolver(dir));
      stream.Initialize(componentContext);
      File.Delete(record);
      var attach = await stream.AttachAsync(perCallContext, "ctr", cancellationToken: TestContext.Current.CancellationToken);
      Assert.True(attach.Success, attach.Error);
      await AssertPerCallHostAsync(record);
      await attach.Data.DisposeAsync();
    }

    [Fact]
    public async Task MachineInspect_ConvertsCapturedMiBMemoryToBytes()
    {
      RequirePosixShellFixture();

      var driver = CreateMachineDriver(ReturnJson(MachineInspectJson));

      var result = await driver.InspectAsync(
          new DriverContext("podman"), "podman-machine-default", TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(2, result.Data.Resources.Cpus);
      Assert.Equal(2147483648L, result.Data.Resources.Memory);
      Assert.Equal(107374182400L, result.Data.Resources.DiskSize);
    }

    [Fact]
    public async Task AutoStartMachine_StartFailureThenPingSuccess_DoesNotThrow()
    {
      RequirePosixShellFixture();

      if (!PodmanCliDriverPack.MachineManagementApplies())
        Assert.Skip("Podman machine auto-start applies only on macOS/Windows");

      var dir = CreateOutputDirectory("podman-machine-race");
      WriteExecutable(Path.Combine(dir, "podman"), """
          #!/bin/sh
          case "$*" in
            *"machine list --format json"*)
              echo '[{"Name":"podman-machine-default","Default":true,"Running":false,"Starting":false}]'
              exit 0
              ;;
            *"machine start podman-machine-default"*)
              echo 'Error: machine "podman-machine-default" is already running' >&2
              exit 125
              ;;
            *"info"*)
              echo 'ok'
              exit 0
              ;;
          esac
          exit 2
          """);
      var pack = new PodmanCliDriverPack();

      var ex = await Record.ExceptionAsync(() => pack.InitializeAsync(new DriverContext("podman")
      {
        SearchPaths = [dir],
        AutoStartMachine = new AutoStartMachineConfig()
      }, TestContext.Current.CancellationToken));

      Assert.Null(ex);
    }

    // Captured with podman version 6.0.0 on 2026-07-03:
    // podman pod create --name fd-wp5-20260703173403-pod
    // podman run -d --name fd-wp5-20260703173403-ctr --pod fd-wp5-20260703173403-pod docker.io/library/alpine:3.20 sleep 300
    // podman pod ps --format json
    private const string PodPsJson = """
        [{
          "Containers": [
            {
              "Id": "d9017a5a328eb7fac74e73b53bbdf5fcbdefb6ccf06fdf32f61e2760dc309c01",
              "Names": "8a271f4b082c-infra",
              "Status": "running",
              "RestartCount": 0
            },
            {
              "Id": "7e29c5d7ade36007d4e7bf802ead186cc59510c4ad17e03a31f84c104feb0154",
              "Names": "fd-wp5-20260703173403-ctr",
              "Status": "running",
              "RestartCount": 0
            }
          ],
          "Created": "2026-07-03T17:34:03.364769201+02:00",
          "Id": "8a271f4b082c7800811db676a84dd0c106d452804b4c9cffce4a877eff8348e1",
          "InfraId": "d9017a5a328eb7fac74e73b53bbdf5fcbdefb6ccf06fdf32f61e2760dc309c01",
          "Name": "fd-wp5-20260703173403-pod",
          "Status": "Running"
        }]
        """;

    // Captured with podman version 6.0.0 on 2026-07-03:
    // podman pod inspect fd-wp5-20260703173403-pod
    private const string PodInspectJson = """
        [{
          "Id": "8a271f4b082c7800811db676a84dd0c106d452804b4c9cffce4a877eff8348e1",
          "Name": "fd-wp5-20260703173403-pod",
          "Created": "2026-07-03T17:34:03.364769201+02:00",
          "State": "Running",
          "Hostname": "",
          "InfraContainerID": "d9017a5a328eb7fac74e73b53bbdf5fcbdefb6ccf06fdf32f61e2760dc309c01",
          "NumContainers": 2,
          "Containers": [
            {
              "Id": "d9017a5a328eb7fac74e73b53bbdf5fcbdefb6ccf06fdf32f61e2760dc309c01",
              "Name": "8a271f4b082c-infra",
              "State": "running"
            },
            {
              "Id": "7e29c5d7ade36007d4e7bf802ead186cc59510c4ad17e03a31f84c104feb0154",
              "Name": "fd-wp5-20260703173403-ctr",
              "State": "running"
            }
          ]
        }]
        """;

    // Captured with podman version 6.0.0 on 2026-07-03:
    // podman machine inspect podman-machine-default
    private const string MachineInspectJson = """
        [{
          "Name": "podman-machine-default",
          "Resources": {
            "CPUs": 2,
            "DiskSize": 100,
            "Memory": 2048
          },
          "State": "running",
          "Rootful": false
        }]
        """;

    private static string ReturnJson(string json) => $"""
        #!/bin/sh
        cat <<'JSON'
        {json}
        JSON
        exit 0
        """;

    private static string FakePodmanForContext(string record) => $$"""
        #!/bin/sh
        printf '%s\n' "$@" > '{{record}}'
        case "$*" in
          *"pod ps --format json"*|*"machine list --format json"*)
            echo '[]'
            exit 0
            ;;
          *"volume inspect"*)
            echo '[{"Name":"vol"}]'
            exit 0
            ;;
          *)
            echo 'ok'
            exit 0
            ;;
        esac
        """;

    private static PodmanCliContainerDriver CreateContainerDriver(string script)
    {
      var dir = CreatePodmanDirectory("podman-container", script);
      var driver = new PodmanCliContainerDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static PodmanCliImageDriver CreateImageDriver(string script)
    {
      var dir = CreatePodmanDirectory("podman-image", script);
      return CreateImageDriver(dir, new DriverContext("podman"));
    }

    private static PodmanCliImageDriver CreateImageDriver(string dir, DriverContext context)
    {
      var driver = new PodmanCliImageDriver(new PodmanResolver(dir));
      driver.Initialize(context);
      return driver;
    }

    private static PodmanCliPodDriver CreatePodDriver(string script)
    {
      var dir = CreatePodmanDirectory("podman-pod", script);
      var driver = new PodmanCliPodDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static PodmanCliNetworkDriver CreateNetworkDriver(string script)
    {
      var dir = CreatePodmanDirectory("podman-network", script);
      var driver = new PodmanCliNetworkDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static PodmanCliVolumeDriver CreateVolumeDriver(string script)
    {
      var dir = CreatePodmanDirectory("podman-volume", script);
      var driver = new PodmanCliVolumeDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static PodmanCliMachineDriver CreateMachineDriver(string script)
    {
      var dir = CreatePodmanDirectory("podman-machine", script);
      var driver = new PodmanCliMachineDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }
    private static string CreatePodmanDirectory(string name, string script)
    {
      var dir = CreateOutputDirectory(name);
      WriteExecutable(Path.Combine(dir, "podman"), script);
      return dir;
    }
    private static string CreateOutputDirectory(string name)
    {
      var dir = Path.Combine(AppContext.BaseDirectory, ".out", name, Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      return dir;
    }
    private static void WriteExecutable(string path, string script)
    {
      File.WriteAllText(path, script.Replace("\r\n", "\n"));
      if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static async Task<string[]> ReadArgsAsync(string record)
    {
      var text = await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken);
      return text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
    }

    private static async Task AssertPerCallHostAsync(string record)
    {
      var args = await ReadLinesEventuallyAsync(record);
      Assert.Contains("--url", args);
      Assert.Contains("tcp://per-call:5678", args);
      Assert.DoesNotContain("tcp://component:1234", args);
    }

    private static async Task<string[]> ReadLinesEventuallyAsync(string record)
    {
      for (var i = 0; i < 50; i++)
      {
        if (File.Exists(record))
          return await File.ReadAllLinesAsync(record, TestContext.Current.CancellationToken);
        await Task.Delay(10, TestContext.Current.CancellationToken);
      }

      return await File.ReadAllLinesAsync(record, TestContext.Current.CancellationToken);
    }

    private static void RequirePosixShellFixture()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");
    }

    private sealed class PodmanResolver(string directory) : IPodmanBinaryResolver
    {
      private readonly PodmanBinary _binary = new(directory, "podman", SudoMechanism.None, null!, PodmanBinaryType.PodmanClient);
      public PodmanBinary[] Binaries => [_binary];
      public PodmanBinary MainPodmanClient => _binary;
      public PodmanBinary PodmanRemote => _binary;
      public PodmanBinary Resolve(string binary) => _binary;
      public string ResolveBinaryPath(string podmanCommand) => _binary.FqPath;
    }
  }
}
