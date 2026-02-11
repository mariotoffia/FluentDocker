using System;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers.Podman;
using Xunit;

namespace FluentDocker.Tests.Integration.PodmanCliDriver
{
  /// <summary>
  /// Integration tests for Podman machine driver.
  /// Requires Podman to be installed.
  /// Machine operations manage Linux VMs and can be slow.
  /// </summary>
  [Collection("PodmanDriver")]
  [Trait("Category", "LongRunning")]
  public class PodmanMachineDriverTests : PodmanDriverTestBase
  {
    #region Query (no VM creation)

    [Fact]
    public async Task List_ReturnsWithoutError()
    {
      var result = await MachineDriver.ListAsync(Context);
      Assert.True(result.Success, $"List failed: {result.Error}");
      Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task Info_ReturnsMachineHostInfo()
    {
      var result = await MachineDriver.InfoAsync(Context);
      Assert.True(result.Success, $"Info failed: {result.Error}");
      Assert.NotNull(result.Data);
      Assert.False(string.IsNullOrEmpty(result.Data.Arch),
          "Arch should be populated");
      Assert.False(string.IsNullOrEmpty(result.Data.OS),
          "OS should be populated");
    }

    [Fact]
    public async Task Inspect_DefaultMachine_ReturnsDetailsIfExists()
    {
      // List machines first to see if default exists
      var listResult = await MachineDriver.ListAsync(Context);
      Assert.True(listResult.Success);

      if (!listResult.Data.Any(m => m.Default))
        throw new SkipException("No default Podman machine available for inspect test");

      var inspectResult = await MachineDriver.InspectAsync(Context);
      Assert.True(inspectResult.Success, $"Inspect failed: {inspectResult.Error}");
      Assert.NotNull(inspectResult.Data);
      Assert.False(string.IsNullOrEmpty(inspectResult.Data.Name));
    }

    [Fact]
    public async Task Ssh_DefaultMachine_ExecutesCommandIfRunning()
    {
      // Check if default machine is running
      var listResult = await MachineDriver.ListAsync(Context);
      Assert.True(listResult.Success);

      var defaultMachine = listResult.Data.FirstOrDefault(m => m.Default && m.Running);
      if (defaultMachine == null)
        throw new SkipException("No running default Podman machine for SSH test");

      var result = await MachineDriver.SshAsync(Context, command: "echo hello");
      Assert.True(result.Success, $"SSH failed: {result.Error}");
      Assert.Contains("hello", result.Data);
    }

    #endregion

    #region Lifecycle (creates VMs — slow)

    [Fact]
    public async Task InitAndRemove_FullLifecycle()
    {
      var machineName = UniqueName("fd-mach");
      try
      {
        // Init with small resources (does not start VM)
        var initResult = await MachineDriver.InitAsync(Context, new MachineInitConfig
        {
          Name = machineName,
          Cpus = 1,
          DiskSizeGiB = 10,
          MemoryMiB = 512,
          Now = false
        });
        Assert.True(initResult.Success, $"Init failed: {initResult.Error}");

        // Verify it appears in list
        var listResult = await MachineDriver.ListAsync(Context);
        Assert.True(listResult.Success);
        Assert.Contains(listResult.Data, m => m.Name == machineName);

        // Inspect (works without starting)
        var inspectResult = await MachineDriver.InspectAsync(Context, machineName);
        Assert.True(inspectResult.Success, $"Inspect failed: {inspectResult.Error}");
        Assert.Equal(machineName, inspectResult.Data.Name);

        // Note: On Apple Hypervisor only one VM can run at a time,
        // so we skip start/stop when another machine is active.
        var hasRunning = listResult.Data.Any(m => m.Running && m.Name != machineName);
        if (!hasRunning)
        {
          var startResult = await MachineDriver.StartAsync(Context, machineName);
          Assert.True(startResult.Success, $"Start failed: {startResult.Error}");

          var stopResult = await MachineDriver.StopAsync(Context, machineName);
          Assert.True(stopResult.Success, $"Stop failed: {stopResult.Error}");
        }
      }
      finally
      {
        try
        { await MachineDriver.StopAsync(Context, machineName); }
        catch { }
        try
        { await MachineDriver.RemoveAsync(Context, machineName, force: true); }
        catch { }
      }
    }

    #endregion

    #region Error handling

    [Fact]
    public async Task Remove_NonExistent_FailsGracefully()
    {
      var fakeName = "nonexistent-" + Guid.NewGuid().ToString("N")[..12];
      var result = await MachineDriver.RemoveAsync(Context, fakeName);
      Assert.False(result.Success);
    }

    [Fact]
    public async Task Stop_NonExistent_FailsGracefully()
    {
      var fakeName = "nonexistent-" + Guid.NewGuid().ToString("N")[..12];
      var result = await MachineDriver.StopAsync(Context, fakeName);
      Assert.False(result.Success);
    }

    [Fact]
    public async Task Inspect_NonExistent_FailsGracefully()
    {
      var fakeName = "nonexistent-" + Guid.NewGuid().ToString("N")[..12];
      var result = await MachineDriver.InspectAsync(Context, fakeName);
      Assert.False(result.Success);
    }

    #endregion
  }
}
