using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FluentDocker.Common;
using FluentDocker.Model.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static FluentDocker.Common.FdOs;

namespace FluentDocker.Drivers.Docker.Cli.Binary
{
  /// <summary>
  /// Resolves the available Docker binaries on the local machine.
  /// Implements IBinaryResolver to provide binary resolution for the CLI driver.
  /// </summary>
  /// <remarks>
  /// v3.0 Note: Docker Machine, Docker Toolbox, and the standalone docker-compose binary
  /// are no longer supported. Only Docker CLI and Docker Compose (docker compose subcommand)
  /// are supported.
  /// </remarks>
  public sealed class DockerBinariesResolver : IBinaryResolver
  {
    private readonly BinaryConfiguration _configuration;
    private readonly ILogger<DockerBinariesResolver> _logger;

    /// <summary>
    /// Creates a new resolver using the provided configuration.
    /// </summary>
    /// <param name="configuration">The binary configuration.</param>
    /// <param name="loggerFactory">Optional logger factory; defaults to
    /// <see cref="NullLoggerFactory.Instance"/> when omitted. The Docker CLI
    /// driver pack supplies the consumer-provided factory automatically.</param>
    public DockerBinariesResolver(BinaryConfiguration configuration, ILoggerFactory loggerFactory = null)
    {
      _configuration = configuration ?? new BinaryConfiguration();
      _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<DockerBinariesResolver>();

      Binaries = [.. ResolveFromPaths(
          _configuration.Sudo,
          _configuration.SudoPassword,
          _configuration.BinaryName,
          _configuration.SearchPaths)];

      MainDockerClient = Binaries.FirstOrDefault(x => x.Type == DockerBinaryType.DockerClient);
      MainDockerCli = Binaries.FirstOrDefault(x => x.Type == DockerBinaryType.Cli);

      if (MainDockerClient == null)
      {
        var reason = "Failed to find docker client binary - please add it to your path";
        var driverId = string.IsNullOrWhiteSpace(_configuration.BinaryName)
            ? "docker" : _configuration.BinaryName;
        _logger.LogError("{Reason}", reason);
        throw new DriverNotAvailableException(driverId, reason);
      }

      _logger.LogDebug("Docker Compose availability is verified lazily when compose commands run");
    }

    /// <summary>
    /// Creates a new resolver with explicit sudo settings and paths.
    /// </summary>
    /// <param name="sudo">The sudo mechanism to use.</param>
    /// <param name="password">The sudo password (if required).</param>
    /// <param name="paths">Custom search paths (uses PATH if empty).</param>
    public DockerBinariesResolver(SudoMechanism sudo, string password, params string[] paths)
        : this(new BinaryConfiguration
        {
          Sudo = sudo,
          SudoPassword = password,
          SearchPaths = paths?.Length > 0 ? paths : null
        })
    {
    }

    /// <inheritdoc />
    public DockerBinary[] Binaries { get; }

    /// <inheritdoc />
    public DockerBinary MainDockerClient { get; }

    /// <inheritdoc />
    public DockerBinary MainDockerCli { get; }

    /// <inheritdoc />
    /// <exception cref="FluentDockerException">
    /// The name is unknown, or the binary was not found on the local system.
    /// </exception>
    public DockerBinary Resolve(string binary)
    {
      ArgumentException.ThrowIfNullOrWhiteSpace(binary);

      // A configured custom client name (nerdctl, finch, …) is resolvable by that name:
      // discovery mapped it to DockerClient, so the Translate-unknown path must not reject
      // the very binary the configuration selected.
      if (MatchesConfiguredClientName(binary))
      {
        return MainDockerClient ?? throw new FluentDockerException(
            $"Could not resolve binary {binary} - is it installed on the local system?");
      }

      DockerBinaryType type;
      try
      {
        type = DockerBinary.Translate(binary);
      }
      catch (ArgumentException ex)
      {
        // Keep the documented exception surface: unknown names are a FluentDockerException,
        // not a raw ArgumentException from the Translate helper.
        throw new FluentDockerException($"Cannot resolve unknown binary {binary}", ex);
      }

      var resolved = type switch
      {
        DockerBinaryType.Compose => MainDockerClient,
        DockerBinaryType.DockerClient => MainDockerClient,
        DockerBinaryType.Cli => MainDockerCli,
        _ => null,
      } ?? throw new FluentDockerException($"Could not resolve binary {binary} - is it installed on the local system?");

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
    /// a sudo prefix is not a valid <see cref="ProcessStartInfo.FileName"/>.
    /// </remarks>
    public string ResolveBinaryPath(string dockerCommand)
    {
      var binary = Resolve(dockerCommand);

      return binary.FqPath;
    }

    private IEnumerable<DockerBinary> ResolveFromPaths(
        SudoMechanism sudo, string password, string binaryName, params string[] paths)
    {
      var isWindows = IsWindows();
      var clientName = string.IsNullOrWhiteSpace(binaryName) ? "docker" : binaryName;
      var isDocker = string.Equals(clientName, "docker", StringComparison.OrdinalIgnoreCase);

      if (paths == null || paths.Length == 0)
      {
        var envpaths = Environment.GetEnvironmentVariable("PATH")?.Split(isWindows ? ';' : ':');
        paths = envpaths ?? [];
      }

      if (paths == null || paths.Length == 0)
        return [];

      // The resolved client binary always maps to DockerClient so MainDockerClient is
      // found, even for docker-compatible CLIs (finch, nerdctl) whose name does not
      // translate to a known DockerBinaryType.
      var clientFile = isWindows ? $"{clientName}.exe" : clientName;

      var list = new List<DockerBinary>();
      foreach (var rawPath in paths)
      {
        var path = StripSurroundingQuotes(rawPath);
        try
        {
          if (!Directory.Exists(path))
          {
            continue;
          }

          if (isWindows)
          {
            list.AddRange(from file in Directory.GetFiles(path, $"{clientName}*.*")
                          let f = Path.GetFileName(file)
                          where f != null && f.Equals(clientFile, StringComparison.OrdinalIgnoreCase)
                          select new DockerBinary(path, f, sudo, password, DockerBinaryType.DockerClient));

            // Docker Desktop's dockercli.exe is docker-specific; skip for custom binaries.
            if (isDocker)
            {
              var dockercli = Path.GetFullPath(Path.Combine(path, "..\\.."));
              if (File.Exists(Path.Combine(dockercli, "dockercli.exe")))
              {
                list.Add(new DockerBinary(dockercli, "dockercli.exe", sudo, password));
              }
            }

            continue;
          }

          list.AddRange(from file in Directory.GetFiles(path, $"{clientName}*")
                        let f = Path.GetFileName(file)
                        where f.Equals(clientFile, StringComparison.Ordinal) && IsExecutable(file)
                        select new DockerBinary(path, f, sudo, password, DockerBinaryType.DockerClient));
        }
        catch (Exception e)
        {
          _logger.LogWarning(e, "Failed to get docker binary from path {Path}", path);
        }
      }

      return list;
    }

    private static string StripSurroundingQuotes(string path)
    {
      return path is { Length: >= 2 } && path[0] == '"' && path[^1] == '"'
          ? path[1..^1]
          : path;
    }

    private static bool IsExecutable(string file)
    {
      if (OperatingSystem.IsWindows())
        return File.Exists(file);

      try
      {
        var mode = File.GetUnixFileMode(file);
        return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
      }
      catch
      {
        return false;
      }
    }
  }
}
