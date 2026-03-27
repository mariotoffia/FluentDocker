using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FluentDocker.Common;
using FluentDocker.Model.Common;
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

    /// <summary>
    /// Creates a new resolver using the provided configuration.
    /// </summary>
    /// <param name="configuration">The binary configuration.</param>
    public DockerBinariesResolver(BinaryConfiguration configuration)
    {
      _configuration = configuration ?? new BinaryConfiguration();

      Binaries = ResolveFromPaths(
          _configuration.Sudo,
          _configuration.SudoPassword,
          _configuration.SearchPaths).ToArray();

      MainDockerClient = Binaries.FirstOrDefault(x => x.Type == DockerBinaryType.DockerClient);
      MainDockerCompose = CheckCompose(_configuration.Sudo, _configuration.SudoPassword);
      MainDockerCli = Binaries.FirstOrDefault(x => x.Type == DockerBinaryType.Cli);

      if (MainDockerClient == null)
      {
        Logger.Log("Failed to find docker client binary - please add it to your path");
        throw new FluentDockerException("Failed to find docker client binary - please add it to your path");
      }

      if (MainDockerCompose == null)
      {
        Logger.Log("Docker Compose (docker compose) is not available - compose features will not work");
      }
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
    public DockerBinary MainDockerCompose { get; }

    /// <inheritdoc />
    public DockerBinary MainDockerCli { get; }

    /// <inheritdoc />
    public bool IsDockerComposeAvailable => MainDockerCompose != null;

    /// <inheritdoc />
    public DockerBinary Resolve(string binary)
    {
      var type = DockerBinary.Translate(binary);

      var resolved = type switch
      {
        DockerBinaryType.Compose => MainDockerCompose,
        DockerBinaryType.DockerClient => MainDockerClient,
        DockerBinaryType.Cli => MainDockerCli,
        _ => throw new FluentDockerException($"Cannot resolve unknown binary {binary}"),
      } ?? throw new FluentDockerException($"Could not resolve binary {binary} - is it installed on the local system?");

      return resolved;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns the binary path with sudo prefix when configured.
    /// The sudo password is never included in the returned string for security reasons.
    /// Use <see cref="Resolve"/> to access the full <see cref="DockerBinary"/> with sudo details.
    /// </remarks>
    public string ResolveBinaryPath(string dockerCommand)
    {
      var binary = Resolve(dockerCommand);

      if (IsWindows() || binary.Sudo == SudoMechanism.None)
        return binary.FqPath;

      return binary.Sudo == SudoMechanism.NoPassword
          ? $"sudo {binary.FqPath}"
          : $"sudo -S {binary.FqPath}";
    }

    private static IEnumerable<DockerBinary> ResolveFromPaths(SudoMechanism sudo, string password, params string[] paths)
    {
      var isWindows = IsWindows();
      if (paths == null || paths.Length == 0)
      {
        var envpaths = Environment.GetEnvironmentVariable("PATH")?.Split(isWindows ? ';' : ':');
        paths = envpaths ?? Array.Empty<string>();
      }

      if (paths == null || paths.Length == 0)
        return Array.Empty<DockerBinary>();

      var list = new List<DockerBinary>();
      foreach (var path in paths)
      {
        try
        {
          if (!Directory.Exists(path))
          {
            continue;
          }

          if (isWindows)
          {
            list.AddRange(from file in Directory.GetFiles(path, "docker*.*")
                          let f = Path.GetFileName(file.ToLower())
                          where f != null && f.Equals("docker.exe", StringComparison.Ordinal)
                          select new DockerBinary(path, f, sudo, password));

            var dockercli = Path.GetFullPath(Path.Combine(path, "..\\.."));
            if (File.Exists(Path.Combine(dockercli, "dockercli.exe")))
            {
              list.Add(new DockerBinary(dockercli, "dockercli.exe", sudo, password));
            }

            continue;
          }

          list.AddRange(from file in Directory.GetFiles(path, "docker*")
                        let f = Path.GetFileName(file)
                        let f2 = f.ToLower()
                        where f2.Equals("docker", StringComparison.Ordinal)
                        select new DockerBinary(path, f, sudo, password));
        }
        catch (Exception e)
        {
          Logger.Log("Failed to get docker binary from path: " + path + Environment.NewLine + e);
        }
      }

      return list;
    }

    private DockerBinary CheckCompose(SudoMechanism sudo, string password)
    {
      if (MainDockerClient == null)
        return null;

      try
      {
        using var process = new Process
        {
          StartInfo = new ProcessStartInfo
          {
            FileName = MainDockerClient.FqPath,
            Arguments = "compose version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
          }
        };

        process.Start();
        // Read stdout and stderr concurrently to avoid deadlock
        // when either pipe buffer fills up.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        // Wait for exit first with timeout — ReadToEndAsync completes
        // only after the process closes its pipes (i.e. exits).
        if (!process.WaitForExit(10_000))
        {
          try
          { process.Kill(); }
          catch { /* best effort */ }
          return null;
        }
        var output = outputTask.GetAwaiter().GetResult();
        errorTask.GetAwaiter().GetResult();

        if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
          return new DockerBinary(
              Path.GetDirectoryName(MainDockerClient.FqPath),
              Path.GetFileName(MainDockerClient.FqPath),
              sudo, password, DockerBinaryType.Compose);
        }
      }
      catch (Exception ex)
      {
        Logger.Log($"Docker Compose plugin is not available: {ex.Message}");
      }

      return null;
    }
  }
}
