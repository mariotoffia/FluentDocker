using System;
using System.Globalization;
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
  [Trait("Requires", "PosixShell")]
  public class PodmanCliChunk3RemediationTests
  {
    [Fact]
    public async Task StreamEventsAsync_ParsesRfc3339TimeWithInvariantCulture()
    {
      RequirePosixShellFixture();
      var oldCulture = CultureInfo.CurrentCulture;
      var oldUiCulture = CultureInfo.CurrentUICulture;
      try
      {
        CultureInfo.CurrentCulture = new CultureInfo("th-TH");
        CultureInfo.CurrentUICulture = new CultureInfo("th-TH");
        var driver = CreateStreamDriver(ReturnStdout("""
            {"time":"2026-07-07 14:30:00Z","Type":"container","Action":"start","Actor":{"ID":"ctr"}}
            """));

        await using var enumerator = driver.StreamEventsAsync(
            new DriverContext("podman"),
            cancellationToken: TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(DateTimeOffset.Parse("2026-07-07 14:30:00Z", CultureInfo.InvariantCulture).UtcDateTime, enumerator.Current.Timestamp);
      }
      finally
      {
        CultureInfo.CurrentCulture = oldCulture;
        CultureInfo.CurrentUICulture = oldUiCulture;
      }
    }

    [Fact]
    public async Task MachineListAsync_ParsesSignedNumericStringsWithInvariantCulture()
    {
      RequirePosixShellFixture();
      var oldCulture = CultureInfo.CurrentCulture;
      try
      {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.PositiveSign = "p";
        CultureInfo.CurrentCulture = culture;
        var driver = CreateMachineDriver(ReturnStdout("""
            [{"Name":"vm1","Memory":"+2147483648","DiskSize":"+53687091200"}]
            """));

        var result = await driver.ListAsync(new DriverContext("podman"), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Data);
        var machine = Assert.Single(result.Data);
        Assert.Equal(2147483648L, machine.Memory);
        Assert.Equal(53687091200L, machine.DiskSize);
      }
      finally
      {
        CultureInfo.CurrentCulture = oldCulture;
      }
    }

    [Fact]
    public async Task NetworkCreateAsync_NullOptionAndLabelCollections_DoNotFailBeforeProcessStarts()
    {
      RequirePosixShellFixture();
      var driver = CreateNetworkDriver("""
          #!/bin/sh
          if [ "$1" = "network" ] && [ "$2" = "inspect" ]; then
            cat <<'EOF'
          [{"id":"net123","name":"net1"}]
          EOF
          else
            echo net123
          fi
          exit 0
          """);

      var result = await driver.CreateAsync(
          new DriverContext("podman"),
          new NetworkCreateConfig
          {
            Name = "net1",
            Options = null!,
            Labels = null!
          },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.NotNull(result.Data);
      Assert.Equal("net123", result.Data.Id);
    }

    [Fact]
    public async Task NetworkCreateAsync_WhenPostCreateInspectFails_ReturnsCreatedName()
    {
      RequirePosixShellFixture();
      var driver = CreateNetworkDriver("""
          #!/bin/sh
          if [ "$1" = "network" ] && [ "$2" = "inspect" ]; then
            echo 'inspect failed' >&2
            exit 1
          fi
          echo net123
          exit 0
          """);

      var result = await driver.CreateAsync(
          new DriverContext("podman"),
          new NetworkCreateConfig { Name = "net1" },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal("net123", result.Data.Id);
    }

    [Fact]
    public async Task SystemInfoAsync_DoesNotUseConmonVersionAsEngineVersion()
    {
      RequirePosixShellFixture();
      var driver = CreateSystemDriver(ReturnStdout("""
          {"host":{"conmon":{"version":"2.1.7"}}}
          """));

      var result = await driver.GetInfoAsync(new DriverContext("podman"), TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.NotNull(result.Data);
      Assert.True(string.IsNullOrEmpty(result.Data.EngineVersion));
    }

    [Fact]
    public async Task LoadAsync_ParsesLegacyLoadedImagesPrefix()
    {
      RequirePosixShellFixture();
      var driver = CreateImageDriver(ReturnStdout("Loaded image(s): localhost/demo:latest,localhost/other:v2"));

      var result = await driver.LoadAsync(new DriverContext("podman"), "image.tar", TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(["localhost/demo:latest", "localhost/other:v2"], result.Data);
    }

    private static PodmanCliStreamDriver CreateStreamDriver(string script)
    {
      var driver = new PodmanCliStreamDriver(new PodmanResolver(CreatePodmanDirectory("podman-chunk3-stream", script)));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static PodmanCliMachineDriver CreateMachineDriver(string script)
    {
      var driver = new PodmanCliMachineDriver(new PodmanResolver(CreatePodmanDirectory("podman-chunk3-machine", script)));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static PodmanCliNetworkDriver CreateNetworkDriver(string script)
    {
      var driver = new PodmanCliNetworkDriver(new PodmanResolver(CreatePodmanDirectory("podman-chunk3-network", script)));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static PodmanCliSystemDriver CreateSystemDriver(string script)
    {
      var driver = new PodmanCliSystemDriver(new PodmanResolver(CreatePodmanDirectory("podman-chunk3-system", script)));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static PodmanCliImageDriver CreateImageDriver(string script)
    {
      var driver = new PodmanCliImageDriver(new PodmanResolver(CreatePodmanDirectory("podman-chunk3-image", script)));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static string ReturnStdout(string output) => $$"""
        #!/bin/sh
        cat <<'EOF'
        {{output}}
        EOF
        exit 0
        """;

    private static string CreatePodmanDirectory(string name, string script)
    {
      var dir = Path.Combine(AppContext.BaseDirectory, ".out", name, Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      WriteExecutable(Path.Combine(dir, "podman"), script);
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
