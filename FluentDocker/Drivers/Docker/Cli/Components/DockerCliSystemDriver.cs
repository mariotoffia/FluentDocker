using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI implementation of ISystemDriver.
  /// </summary>
  public partial class DockerCliSystemDriver : DockerCliDriverBase, ISystemDriver
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
        var result = await ExecuteCommandAsync(context, "info --format \"{{json .}}\"", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<SystemInfo>.Fail(
              ErrorOrDefault(result, "System info failed"),
              FailureCode(result.Error, ErrorCodes.General.Unknown),
              CreateErrorContext(context, "GetInfo", result),
              result.ExitCode);
        }

        if (!JsonHelper.TryDeserialize<DockerSystemInfo>(result.Output, out var info, out var parseError))
        {
          return CommandResponse<SystemInfo>.Fail(
              $"System info JSON parsing failed: {parseError?.Message}",
              ErrorCodes.General.Unknown);
        }

        info ??= new DockerSystemInfo();
        info.PopulateMeta();
        return CommandResponse<SystemInfo>.Ok(info);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<SystemInfo>.Fail(ex.Message, FailureCode(ex, ErrorCodes.General.Unknown));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<VersionInfo>> GetVersionAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(context, "version --format \"{{json .}}\"", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<VersionInfo>.Fail(
              ErrorOrDefault(result, "Version check failed"),
              FailureCode(result.Error, ErrorCodes.General.Unknown),
              CreateErrorContext(context, "GetVersion", result),
              result.ExitCode);
        }

        if (!JsonHelper.TryDeserialize<DockerVersionInfo>(result.Output, out var version, out var parseError))
        {
          return CommandResponse<VersionInfo>.Fail(
              $"Docker version JSON parsing failed: {parseError?.Message}",
              ErrorCodes.General.Unknown);
        }

        version ??= new DockerVersionInfo();
        version.PopulateMeta();
        return CommandResponse<VersionInfo>.Ok(version);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<VersionInfo>.Fail(ex.Message, FailureCode(ex, ErrorCodes.General.Unknown));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PingAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        // A liveness probe against a wedged daemon must fail in seconds, not hang the readiness
        // loop for the 5-min buffered default (DCLI-MAJ-3). Cap at 10s (honor a smaller caller
        // RequestTimeout). Mirrors the model runner's BackendProbeTimeout.
        var probeCeiling = TimeSpan.FromSeconds(10);
        var probeTimeout = context?.RequestTimeout is { } rt && rt < probeCeiling ? rt : probeCeiling;
        var result = await ExecuteCommandAsync(context, "version", probeTimeout, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                ErrorOrDefault(result, "Docker daemon not reachable"),
                FailureCode(result.Error, ErrorCodes.General.Unknown),
                CreateErrorContext(context, "Ping", result),
                result.ExitCode);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.General.Unknown));
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
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<bool>.Fail(ex.Message, FailureCode(ex, ErrorCodes.General.Unknown));
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
          return CommandResponse<bool>.Fail(versionResult.Error, versionResult.ErrorCode, versionResult.ExitCode);

        var isLinux = !versionResult.Data?.Os?.Equals("windows", StringComparison.OrdinalIgnoreCase) ?? true;
        return CommandResponse<bool>.Ok(isLinux);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        Logger.LogError(ex, "Linux engine detection failed");
        return CommandResponse<bool>.Fail(ex.Message, FailureCode(ex, ErrorCodes.General.Unknown));
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
        var result = await ExecuteCommandAsync(context, "system df --format \"{{json .}}\"", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<DiskUsageInfo>.Fail(
              ErrorOrDefault(result, "Disk usage failed"),
              FailureCode(result.Error, ErrorCodes.General.Unknown));
        }

        var info = ParseDiskUsageOutput(result.Output, Logger);
        return CommandResponse<DiskUsageInfo>.Ok(info);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<DiskUsageInfo>.Fail(ex.Message, FailureCode(ex, ErrorCodes.General.Unknown));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<SystemPruneResult>> PruneAsync(
        DriverContext context,
        SystemPruneConfig? config = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "system prune -f";
        if (config?.All == true)
          args += " -a";
        if (config?.Volumes == true)
          args += " --volumes";

        // `system prune -a --volumes` on a loaded host routinely exceeds the 5-min buffered
        // default and would be falsely killed mid-reclaim (DCLI-MAJ-2). Give it a generous 30-min
        // ceiling (still bounded, unlike the unbounded path) unless the caller set RequestTimeout.
        var pruneTimeout = context?.RequestTimeout ?? TimeSpan.FromMinutes(30);
        var result = await ExecuteCommandAsync(context, args, pruneTimeout, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<SystemPruneResult>.Fail(
              ErrorOrDefault(result, "System prune failed"),
              FailureCode(result.Error, ErrorCodes.General.Unknown));
        }

        return CommandResponse<SystemPruneResult>.Ok(
            CliPruneOutputParser.ParseSystemPruneOutput(result.Output));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<SystemPruneResult>.Fail(ex.Message, FailureCode(ex, ErrorCodes.General.Unknown));
      }
    }

    #endregion

    #region Disk Usage Parsing

    /// <summary>
    /// Parses Docker CLI <c>system df --format "{{json .}}"</c> output.
    /// Each line is a JSON object with Type, TotalCount, Active, Size, Reclaimable.
    /// </summary>
    public static DiskUsageInfo ParseDiskUsageOutput(string output, ILogger logger = null)
    {
      logger ??= NullLogger.Instance;
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
          logger.LogError(ex, "Disk usage JSON parsing failed");
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
    /// </summary>
    public static long ParseHumanReadableBytes(string value) =>
        CliByteParser.ParseHumanReadableBytes(value);

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
