using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using System.Text.Json;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI implementation of ISystemDriver.
  /// </summary>
  public class DockerCliSystemDriver : DockerCliDriverBase, ISystemDriver
  {
    private static readonly char[] LineSeparators = ['\n', '\r'];
    /// <summary>
    /// Creates a new instance with the specified binary resolver.
    /// </summary>
    public DockerCliSystemDriver(IBinaryResolver binaryResolver) : base(binaryResolver)
    {
    }

    #region Information Operations

    /// <inheritdoc />
    public async Task<CommandResponse<SystemInfo>> GetInfoAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync("info --format \"{{json .}}\"", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<SystemInfo>.Fail(
              result.Error ?? "System info failed",
              ErrorCodes.General.Unknown);
        }

        var info = JsonSerializer.Deserialize<DockerSystemInfo>(result.Output, JsonHelper.CaseInsensitiveOptions) ?? new DockerSystemInfo();
        info.PopulateMeta();
        return CommandResponse<SystemInfo>.Ok(info);
      }
      catch (Exception ex)
      {
        return CommandResponse<SystemInfo>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<VersionInfo>> GetVersionAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync("version --format \"{{json .}}\"", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<VersionInfo>.Fail(
              result.Error ?? "Version check failed",
              ErrorCodes.General.Unknown);
        }

        var version = JsonSerializer.Deserialize<DockerVersionInfo>(result.Output, JsonHelper.CaseInsensitiveOptions) ?? new DockerVersionInfo();
        version.PopulateMeta();
        return CommandResponse<VersionInfo>.Ok(version);
      }
      catch (Exception ex)
      {
        return CommandResponse<VersionInfo>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PingAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync("version", cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail("Docker daemon not reachable", ErrorCodes.General.Unknown);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<bool>> IsWindowsEngineAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var versionResult = await GetVersionAsync(context, cancellationToken).ConfigureAwait(false);
        if (!versionResult.Success)
          return CommandResponse<bool>.Fail(versionResult.Error, versionResult.ErrorCode);

        var isWindows = versionResult.Data?.Os?.Equals("windows", StringComparison.OrdinalIgnoreCase) ?? false;
        return CommandResponse<bool>.Ok(isWindows);
      }
      catch (Exception ex)
      {
        return CommandResponse<bool>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<bool>> IsLinuxEngineAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var versionResult = await GetVersionAsync(context, cancellationToken).ConfigureAwait(false);
        if (!versionResult.Success)
          return CommandResponse<bool>.Ok(true); // Default to Linux

        var isLinux = !versionResult.Data?.Os?.Equals("windows", StringComparison.OrdinalIgnoreCase) ?? true;
        return CommandResponse<bool>.Ok(isLinux);
      }
      catch (Exception ex)
      {
        Logger.Log($"Windows engine detection failed: {ex.Message}");
        return CommandResponse<bool>.Ok(true); // Default to Linux
      }
    }

    #endregion

    #region Maintenance Operations

    /// <inheritdoc />
    public async Task<CommandResponse<DiskUsageInfo>> GetDiskUsageAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync("system df --format \"{{json .}}\"", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<DiskUsageInfo>.Fail(
              result.Error ?? "Disk usage failed",
              ErrorCodes.General.Unknown);
        }

        var info = ParseDiskUsageOutput(result.Output);
        return CommandResponse<DiskUsageInfo>.Ok(info);
      }
      catch (Exception ex)
      {
        return CommandResponse<DiskUsageInfo>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<SystemPruneResult>> PruneAsync(
        DriverContext context,
        SystemPruneConfig config = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "system prune -f";
        if (config?.All == true)
          args += " -a";
        if (config?.Volumes == true)
          args += " --volumes";

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<SystemPruneResult>.Fail(
              result.Error ?? "System prune failed",
              ErrorCodes.General.Unknown);
        }

        return CommandResponse<SystemPruneResult>.Ok(
            CliPruneOutputParser.ParseSystemPruneOutput(result.Output));
      }
      catch (Exception ex)
      {
        return CommandResponse<SystemPruneResult>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    #endregion

    #region Daemon Operations (Docker Desktop specific)

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> SwitchDaemonAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteDockerCliCommandAsync("-SwitchDaemon", cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(result.Error ?? "Switch daemon failed", ErrorCodes.General.Unknown);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> SwitchToLinuxDaemonAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteDockerCliCommandAsync("-SwitchLinuxEngine", cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(result.Error ?? "Switch to Linux failed", ErrorCodes.General.Unknown);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> SwitchToWindowsDaemonAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteDockerCliCommandAsync("-SwitchWindowsEngine", cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(result.Error ?? "Switch to Windows failed", ErrorCodes.General.Unknown);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Executes a Docker CLI command (Docker Desktop specific).
    /// </summary>
    private async Task<SimpleCommandResult> ExecuteDockerCliCommandAsync(string arguments, CancellationToken cancellationToken)
    {
      return await Task.Run(() =>
      {
        try
        {
          var process = new Process
          {
            StartInfo = new ProcessStartInfo
            {
              FileName = BinaryResolver?.ResolveBinaryPath("dockercli") ?? "dockercli",
              Arguments = arguments,
              RedirectStandardOutput = true,
              RedirectStandardError = true,
              UseShellExecute = false,
              CreateNoWindow = true
            }
          };

          var output = new StringBuilder();
          var error = new StringBuilder();

          process.OutputDataReceived += (s, e) =>
          {
            if (!string.IsNullOrEmpty(e.Data))
              output.AppendLine(e.Data);
          };

          process.ErrorDataReceived += (s, e) =>
          {
            if (!string.IsNullOrEmpty(e.Data))
              error.AppendLine(e.Data);
          };

          process.Start();
          process.BeginOutputReadLine();
          process.BeginErrorReadLine();

          while (!process.WaitForExit(1000))
          {
            cancellationToken.ThrowIfCancellationRequested();
          }

          return new SimpleCommandResult
          {
            Success = process.ExitCode == 0,
            Output = output.ToString(),
            Error = error.ToString(),
            ExitCode = process.ExitCode
          };
        }
        catch (Exception ex)
        {
          return new SimpleCommandResult
          {
            Success = false,
            Error = ex.Message,
            ExitCode = -1
          };
        }
      }, cancellationToken);
    }

    #endregion

    #region Disk Usage Parsing

    /// <summary>
    /// Parses Docker CLI <c>system df --format "{{json .}}"</c> output.
    /// Each line is a JSON object with Type, TotalCount, Active, Size, Reclaimable.
    /// </summary>
    public static DiskUsageInfo ParseDiskUsageOutput(string output)
    {
      var info = new DiskUsageInfo();
      if (string.IsNullOrWhiteSpace(output))
        return info;

      var lines = output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
      foreach (var line in lines)
      {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '{')
          continue;

        try
        {
          var obj = JsonHelper.ParseElement(trimmed);
          var type = obj.GetStringOrDefault("Type") ?? "";
          var item = new DiskUsageItem
          {
            TotalCount = obj.GetInt32OrDefault("TotalCount"),
            Active = obj.GetInt32OrDefault("Active"),
            Size = ParseHumanReadableBytes(obj.GetStringOrDefault("Size")),
            Reclaimable = ParseReclaimableBytes(obj.GetStringOrDefault("Reclaimable"))
          };

          switch (type)
          {
            case "Images":
              info.Images = item;
              break;
            case "Containers":
              info.Containers = item;
              break;
            case "Local Volumes":
              info.Volumes = item;
              break;
            case "Build Cache":
              info.BuildCache = item;
              break;
          }
        }
        catch (Exception ex)
        {
          Logger.Log($"Disk usage JSON parsing failed: {ex.Message}");
        }
      }

      info.TotalSize = info.Images.Size + info.Containers.Size
                       + info.Volumes.Size + info.BuildCache.Size;
      info.Reclaimable = info.Images.Reclaimable + info.Containers.Reclaimable
                         + info.Volumes.Reclaimable + info.BuildCache.Reclaimable;
      return info;
    }

    /// <summary>
    /// Parses a human-readable byte string (e.g. "1.234GB", "500MB", "0B").
    /// Uses base-1000 for B/kB/KB/MB/GB/TB and base-1024 for KiB/MiB/GiB/TiB.
    /// </summary>
    public static long ParseHumanReadableBytes(string value)
    {
      if (string.IsNullOrWhiteSpace(value))
        return 0;

      var s = value.Trim();
      var suffixes = new (string suffix, double multiplier)[]
      {
                ("TiB", 1024.0 * 1024 * 1024 * 1024),
                ("GiB", 1024.0 * 1024 * 1024),
                ("MiB", 1024.0 * 1024),
                ("KiB", 1024.0),
                ("TB", 1000.0 * 1000 * 1000 * 1000),
                ("GB", 1000.0 * 1000 * 1000),
                ("MB", 1000.0 * 1000),
                ("kB", 1000.0),
                ("KB", 1000.0),
                ("B", 1.0)
      };

      foreach (var (suffix, multiplier) in suffixes)
      {
        if (!s.EndsWith(suffix, StringComparison.Ordinal))
          continue;

        var numStr = s.Substring(0, s.Length - suffix.Length).Trim();
        if (double.TryParse(numStr, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var num))
          return (long)(num * multiplier);
        return 0;
      }

      // No recognized suffix: try raw number
      return double.TryParse(s, NumberStyles.Float,
                 CultureInfo.InvariantCulture, out var raw)
          ? (long)raw
          : 0;
    }

    /// <summary>
    /// Parses a reclaimable size string that may include a percentage suffix,
    /// e.g. "500MB (40%)". Strips the parenthesized portion and parses the
    /// remaining human-readable byte value.
    /// </summary>
    public static long ParseReclaimableBytes(string value)
    {
      if (string.IsNullOrWhiteSpace(value))
        return 0;

      var s = value.Trim();

      // Strip trailing " (N%)" if present
      var parenIndex = s.IndexOf('(');
      if (parenIndex >= 0)
        s = s.Substring(0, parenIndex).Trim();

      return ParseHumanReadableBytes(s);
    }

    #endregion
  }
}
