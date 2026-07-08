using System.ComponentModel;
using System.Diagnostics;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman.Cli
{
  public abstract partial class PodmanCliDriverBase
  {
    private static void StartProcessOrThrow(Process process, string binaryPath)
    {
      try
      {
        process.Start();
      }
      catch (Win32Exception ex)
      {
        process.Dispose();
        throw new DriverException(
            $"Failed to start Podman CLI process '{process.StartInfo.FileName}' for binary '{binaryPath}'. Ensure the binary exists and is executable.",
            ErrorCodes.Driver.CommandExecutionFailed,
            ex);
      }
    }
  }
}
