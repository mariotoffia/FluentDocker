using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentDocker.Common;
using FluentDocker.Model.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static FluentDocker.Common.FdOs;

namespace FluentDocker.Drivers.Podman.Cli.Binary
{
  /// <summary>
  /// Resolves the available Podman binaries on the local machine.
  /// </summary>
  public sealed class PodmanBinariesResolver : IPodmanBinaryResolver
  {
    private readonly PodmanBinaryConfiguration _configuration;
    private readonly ILogger<PodmanBinariesResolver> _logger;

    /// <summary>
    /// Creates a new resolver using the provided configuration.
    /// </summary>
    /// <param name="configuration">The binary configuration.</param>
    /// <param name="loggerFactory">Optional logger factory; defaults to
    /// <see cref="NullLoggerFactory.Instance"/>. The Podman CLI driver pack supplies
    /// the consumer-provided factory automatically.</param>
    public PodmanBinariesResolver(PodmanBinaryConfiguration configuration, ILoggerFactory loggerFactory = null)
    {
      _configuration = configuration ?? new PodmanBinaryConfiguration();
      _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<PodmanBinariesResolver>();

      Binaries = [.. ResolveFromPaths(
          _configuration.Sudo,
          _configuration.SudoPassword,
          _configuration.SearchPaths)];
      MainPodmanClient = Binaries.FirstOrDefault(x => x.Type == PodmanBinaryType.PodmanClient);
      PodmanRemote = Binaries.FirstOrDefault(x => x.Type == PodmanBinaryType.PodmanRemote);

      if (MainPodmanClient == null)
      {
        const string reason = "Failed to find podman client binary - please add it to your path";
        var driverId = string.IsNullOrWhiteSpace(_configuration.BinaryName)
            ? "podman" : _configuration.BinaryName;
        _logger.LogError("{Reason}", reason);
        throw new DriverNotAvailableException(driverId, reason);
      }
    }

    /// <summary>
    /// Creates a new resolver with explicit sudo settings and paths.
    /// </summary>
    public PodmanBinariesResolver(SudoMechanism sudo, string password, params string[] paths)
        : this(new PodmanBinaryConfiguration
        {
          Sudo = sudo,
          SudoPassword = password,
          SearchPaths = paths?.Length > 0 ? paths : null
        })
    {
    }

    /// <inheritdoc />
    public PodmanBinary[] Binaries { get; }

    /// <inheritdoc />
    public PodmanBinary MainPodmanClient { get; }

    /// <inheritdoc />
    public PodmanBinary PodmanRemote { get; }

    /// <inheritdoc />
    public PodmanBinary Resolve(string binary)
    {
      var type = PodmanBinary.Translate(binary);

      var resolved = type switch
      {
        PodmanBinaryType.PodmanClient => MainPodmanClient,
        PodmanBinaryType.PodmanRemote => PodmanRemote,
        _ => throw new FluentDockerException($"Cannot resolve unknown binary {binary}"),
      } ?? throw new FluentDockerException(
            $"Could not resolve binary {binary} - is it installed on the local system?");

      return resolved;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns only the executable path. Use <see cref="Resolve"/> to access sudo details;
    /// a sudo prefix is not a valid <see cref="System.Diagnostics.ProcessStartInfo.FileName"/>.
    /// </remarks>
    public string ResolveBinaryPath(string podmanCommand)
    {
      var binary = Resolve(podmanCommand);

      return binary.FqPath;
    }

    private IEnumerable<PodmanBinary> ResolveFromPaths(
        SudoMechanism sudo, string password, params string[] paths)
    {
      var isWindows = IsWindows();
      if (paths == null || paths.Length == 0)
      {
        var envpaths = Environment.GetEnvironmentVariable("PATH")
            ?.Split(isWindows ? ';' : ':');
        paths = envpaths ?? [];
      }

      if (paths == null || paths.Length == 0)
        return [];

      // The configured client name (default "podman"); the remote client name is fixed.
      var clientName = string.IsNullOrWhiteSpace(_configuration.BinaryName)
          ? "podman" : _configuration.BinaryName;
      var clientFile = isWindows ? clientName + ".exe" : clientName;
      const string remoteName = "podman-remote";
      var remoteFile = isWindows ? remoteName + ".exe" : remoteName;

      PodmanBinary Make(string dir, string fileName)
      {
        var name = Path.GetFileName(fileName);
        var type = name.Equals(remoteFile, StringComparison.OrdinalIgnoreCase)
            ? PodmanBinaryType.PodmanRemote
            : PodmanBinaryType.PodmanClient;
        return new PodmanBinary(dir, name, sudo, password, type);
      }

      var list = new List<PodmanBinary>();
      foreach (var path in paths)
      {
        try
        {
          if (!Directory.Exists(path))
            continue;

          var comparison = isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
          list.AddRange(from file in Directory.GetFiles(path)
                        let f = Path.GetFileName(file)
                        where f.Equals(clientFile, comparison)
                            || f.Equals(remoteFile, comparison)
                        select Make(path, file));
        }
        catch (Exception e)
        {
          _logger.LogWarning(e, "Failed to get podman binary from path {Path}", path);
        }
      }

      return list;
    }
  }
}
