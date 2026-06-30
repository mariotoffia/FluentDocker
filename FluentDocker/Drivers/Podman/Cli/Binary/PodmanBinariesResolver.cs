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
        _logger.LogError("Failed to find podman client binary - please add it to your path");
        throw new FluentDockerException(
            "Failed to find podman client binary - please add it to your path");
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
    /// Returns the binary path with sudo prefix when configured.
    /// The sudo password is never included in the returned string for security reasons.
    /// Use <see cref="Resolve"/> to access the full <see cref="PodmanBinary"/> with sudo details.
    /// </remarks>
    public string ResolveBinaryPath(string podmanCommand)
    {
      var binary = Resolve(podmanCommand);

      if (IsWindows() || binary.Sudo == SudoMechanism.None)
        return binary.FqPath;

      return binary.Sudo == SudoMechanism.NoPassword
          ? $"sudo {binary.FqPath}"
          : $"sudo -S {binary.FqPath}";
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
          ? "podman" : _configuration.BinaryName.ToLowerInvariant();
      var clientFile = isWindows ? clientName + ".exe" : clientName;
      const string remoteName = "podman-remote";
      var remoteFile = isWindows ? remoteName + ".exe" : remoteName;

      PodmanBinary Make(string dir, string fileName)
      {
        var lower = Path.GetFileName(fileName).ToLowerInvariant();
        var type = lower.Equals(remoteFile, StringComparison.Ordinal)
            ? PodmanBinaryType.PodmanRemote
            : PodmanBinaryType.PodmanClient;
        return new PodmanBinary(dir, Path.GetFileName(fileName), sudo, password, type);
      }

      var list = new List<PodmanBinary>();
      foreach (var path in paths)
      {
        try
        {
          if (!Directory.Exists(path))
            continue;

          list.AddRange(from file in Directory.GetFiles(path)
                        let f = Path.GetFileName(file).ToLowerInvariant()
                        where f.Equals(clientFile, StringComparison.Ordinal)
                            || f.Equals(remoteFile, StringComparison.Ordinal)
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
