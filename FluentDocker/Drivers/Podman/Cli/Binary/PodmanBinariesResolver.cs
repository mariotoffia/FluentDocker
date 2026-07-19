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
    public PodmanBinariesResolver(PodmanBinaryConfiguration configuration, ILoggerFactory? loggerFactory = null)
    {
      _configuration = configuration ?? new PodmanBinaryConfiguration();
      _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<PodmanBinariesResolver>();

      Binaries = [.. ResolveFromPaths(
          _configuration.Sudo,
          _configuration.SudoPassword,
          _configuration.SearchPaths)];
      // Validated non-null by the guard below before the constructor returns; the interface
      // exposes MainPodmanClient as non-null, so keep the invariant rather than widen the type.
      MainPodmanClient = Binaries.FirstOrDefault(x => x.Type == PodmanBinaryType.PodmanClient)!;
      // The remote client is optional; the interface contract is non-null and the only reader
      // (Resolve) guards with '?? throw', so conform to that contract here.
      PodmanRemote = Binaries.FirstOrDefault(x => x.Type == PodmanBinaryType.PodmanRemote)!;

      if (MainPodmanClient == null)
      {
        var driverId = string.IsNullOrWhiteSpace(_configuration.BinaryName)
            ? "podman" : _configuration.BinaryName;
        var reason = IsRemoteClientBinary(driverId)
            ? $"'{driverId}' is a remote client, not a podman client binary; configure WithBinary(\"podman\") or another local podman client binary."
            : "Failed to find podman client binary - please add it to your path";
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
    /// <exception cref="FluentDockerException">
    /// The name is unknown, or the binary was not found on the local system.
    /// </exception>
    public PodmanBinary Resolve(string binary)
    {
      ArgumentException.ThrowIfNullOrWhiteSpace(binary);

      // A configured custom client name (podman5, …) is resolvable by that name: discovery
      // mapped it to PodmanClient, so the Translate-unknown path must not reject the very
      // binary the configuration selected.
      if (MatchesConfiguredClientName(binary))
      {
        return MainPodmanClient ?? throw new FluentDockerException(
            $"Could not resolve binary {binary} - is it installed on the local system?");
      }

      PodmanBinaryType type;
      try
      {
        type = PodmanBinary.Translate(binary);
      }
      catch (ArgumentException ex)
      {
        // Keep the documented exception surface: unknown names are a FluentDockerException,
        // not a raw ArgumentException from the Translate helper.
        throw new FluentDockerException($"Cannot resolve unknown binary {binary}", ex);
      }

      var resolved = type switch
      {
        PodmanBinaryType.PodmanClient => MainPodmanClient,
        PodmanBinaryType.PodmanRemote => PodmanRemote,
        _ => null,
      } ?? throw new FluentDockerException(
            $"Could not resolve binary {binary} - is it installed on the local system?");

      return resolved;
    }

    private bool MatchesConfiguredClientName(string binary)
    {
      if (string.IsNullOrWhiteSpace(_configuration.BinaryName))
        return false;

      static string Normalize(string name) =>
          name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
      return string.Equals(
          Normalize(binary), Normalize(_configuration.BinaryName), StringComparison.OrdinalIgnoreCase);
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

    private static bool IsRemoteClientBinary(string binary)
    {
      var name = Path.GetFileName(binary);
      return name.Equals("podman-remote", StringComparison.OrdinalIgnoreCase)
          || name.Equals("podman-remote.exe", StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<PodmanBinary> ResolveFromPaths(
        SudoMechanism sudo, string? password, params string[]? paths)
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
        return new PodmanBinary(dir, name, sudo, password!, type);
      }

      var list = new List<PodmanBinary>();
      foreach (var path in paths)
      {
        try
        {
          if (!Directory.Exists(path))
            continue;

          AddIfExecutable(list, path, clientFile, isWindows, Make);
          AddIfExecutable(list, path, remoteFile, isWindows, Make);
        }
        catch (Exception e)
        {
          _logger.LogWarning(e, "Failed to get podman binary from path {Path}", path);
        }
      }

      return list;
    }

    private static void AddIfExecutable(
        List<PodmanBinary> list, string directory, string fileName, bool isWindows,
        Func<string, string, PodmanBinary> make)
    {
      var fullPath = Path.Combine(directory, fileName);
      if (File.Exists(fullPath) && (isWindows || HasUnixExecuteBit(fullPath)))
        list.Add(make(directory, fileName));
    }

    private static bool HasUnixExecuteBit(string path)
    {
      if (OperatingSystem.IsWindows())
        return true;

      var mode = File.GetUnixFileMode(path);
      return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
    }
  }
}
