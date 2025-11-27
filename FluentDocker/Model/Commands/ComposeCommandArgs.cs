using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentDocker.Extensions;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Images;

namespace FluentDocker.Model.Commands
{
  /// <summary>
  /// Arguments for docker compose build command.
  /// </summary>
  public struct ComposeBuildCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Always remove intermediate containers.</summary>
    public bool ForceRm { get; set; }
    /// <summary>Do not use cache when building the image.</summary>
    public bool NoCache { get; set; }
    /// <summary>Always attempt to pull a newer version of the image.</summary>
    public bool Pull { get; set; }
    /// <summary>Build images in parallel.</summary>
    public bool Parallel { get; set; }
    /// <summary>Set memory limit for the build container.</summary>
    public string Memory { get; set; }
    /// <summary>Set build-time variables.</summary>
    public IList<string> BuildArgs { get; set; }
    /// <summary>Compress the build context using gzip.</summary>
    public bool Compress { get; set; }
    /// <summary>Print progress in plain text mode.</summary>
    public string Progress { get; set; }
    /// <summary>Don't print anything to STDOUT.</summary>
    public bool Quiet { get; set; }
    /// <summary>Services to build.</summary>
    public IList<string> Services { get; set; }
    /// <summary>Environment variables to set during build.</summary>
    public IDictionary<string, string> Env { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (ForceRm)
        sb.Append(" --force-rm");
      if (NoCache)
        sb.Append(" --no-cache");
      if (Pull)
        sb.Append(" --pull");
      if (Parallel)
        sb.Append(" --parallel");
      sb.OptionIfExists("--memory ", Memory);
      sb.OptionIfExists("--build-arg ", BuildArgs?.ToArray());
      if (Compress)
        sb.Append(" --compress");
      sb.OptionIfExists("--progress ", Progress);
      if (Quiet)
        sb.Append(" --quiet");

      if (Services != null && Services.Count > 0)
        sb.Append(" " + string.Join(" ", Services));

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose create command.
  /// </summary>
  public struct ComposeCreateCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Recreate containers even if their configuration has not changed.</summary>
    public bool ForceRecreate { get; set; }
    /// <summary>If containers already exist, don't recreate them.</summary>
    public bool NoRecreate { get; set; }
    /// <summary>Don't build an image, even if it's missing.</summary>
    public bool NoBuild { get; set; }
    /// <summary>Build images before creating containers.</summary>
    public bool Build { get; set; }
    /// <summary>Pull images before creating containers.</summary>
    public string Pull { get; set; }
    /// <summary>Remove containers for services not defined in the Compose file.</summary>
    public bool RemoveOrphans { get; set; }
    /// <summary>Scale SERVICE to NUM instances.</summary>
    public IList<string> Scale { get; set; }
    /// <summary>Services to create.</summary>
    public IList<string> Services { get; set; }
    /// <summary>Environment variables.</summary>
    public IDictionary<string, string> Env { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (ForceRecreate)
        sb.Append(" --force-recreate");
      if (NoRecreate)
        sb.Append(" --no-recreate");
      if (NoBuild)
        sb.Append(" --no-build");
      if (Build)
        sb.Append(" --build");
      sb.OptionIfExists("--pull ", Pull);
      if (RemoveOrphans)
        sb.Append(" --remove-orphans");
      sb.OptionIfExists("--scale ", Scale?.ToArray());

      if (Services != null && Services.Count > 0)
        sb.Append(" " + string.Join(" ", Services));

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose start command.
  /// </summary>
  public struct ComposeStartCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Services to start.</summary>
    public IList<string> Services { get; set; }
    /// <summary>Environment variables.</summary>
    public IDictionary<string, string> Env { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Services != null && Services.Count > 0)
        sb.Append(" " + string.Join(" ", Services));

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose stop command.
  /// </summary>
  public struct ComposeStopCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Specify a shutdown timeout in seconds.</summary>
    public TimeSpan? Timeout { get; set; }
    /// <summary>Services to stop.</summary>
    public IList<string> Services { get; set; }
    /// <summary>Environment variables.</summary>
    public IDictionary<string, string> Env { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Timeout.HasValue)
        sb.Append($" -t {Math.Round(Timeout.Value.TotalSeconds, 0)}");

      if (Services != null && Services.Count > 0)
        sb.Append(" " + string.Join(" ", Services));

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose kill command.
  /// </summary>
  public struct ComposeKillCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Signal to send to the container.</summary>
    public UnixSignal Signal { get; set; }
    /// <summary>Remove containers for services not defined in the Compose file.</summary>
    public bool RemoveOrphans { get; set; }
    /// <summary>Services to kill.</summary>
    public IList<string> Services { get; set; }
    /// <summary>Environment variables.</summary>
    public IDictionary<string, string> Env { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Signal != UnixSignal.SIGKILL)
        sb.Append($" -s {Signal}");
      if (RemoveOrphans)
        sb.Append(" --remove-orphans");

      if (Services != null && Services.Count > 0)
        sb.Append(" " + string.Join(" ", Services));

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose pause command.
  /// </summary>
  public struct ComposePauseCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Services to pause.</summary>
    public IList<string> Services { get; set; }
    /// <summary>Environment variables.</summary>
    public IDictionary<string, string> Env { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Services != null && Services.Count > 0)
        sb.Append(" " + string.Join(" ", Services));

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose unpause command.
  /// </summary>
  public struct ComposeUnpauseCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Services to unpause.</summary>
    public IList<string> Services { get; set; }
    /// <summary>Environment variables.</summary>
    public IDictionary<string, string> Env { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Services != null && Services.Count > 0)
        sb.Append(" " + string.Join(" ", Services));

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose restart command.
  /// </summary>
  public struct ComposeRestartCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Specify a shutdown timeout in seconds.</summary>
    public TimeSpan? Timeout { get; set; }
    /// <summary>Don't start linked services.</summary>
    public bool NoDeps { get; set; }
    /// <summary>Container IDs or service names to restart.</summary>
    public IList<string> Services { get; set; }
    /// <summary>Environment variables.</summary>
    public IDictionary<string, string> Env { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Timeout.HasValue)
        sb.Append($" -t {Math.Round(Timeout.Value.TotalSeconds, 0)}");
      if (NoDeps)
        sb.Append(" --no-deps");

      if (Services != null && Services.Count > 0)
        sb.Append(" " + string.Join(" ", Services));

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose down command.
  /// </summary>
  public struct ComposeDownCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Remove images used by services. Values: all, local.</summary>
    public ImageRemovalOption RemoveImages { get; set; }
    /// <summary>Remove named volumes declared in the volumes section.</summary>
    public bool RemoveVolumes { get; set; }
    /// <summary>Remove containers for services not defined in the Compose file.</summary>
    public bool RemoveOrphans { get; set; }
    /// <summary>Specify a shutdown timeout in seconds.</summary>
    public TimeSpan? Timeout { get; set; }
    /// <summary>Environment variables.</summary>
    public IDictionary<string, string> Env { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (RemoveOrphans)
        sb.Append(" --remove-orphans");
      if (RemoveVolumes)
        sb.Append(" -v");
      if (RemoveImages != ImageRemovalOption.None)
        sb.Append(RemoveImages == ImageRemovalOption.Local ? " --rmi local" : " --rmi all");
      if (Timeout.HasValue)
        sb.Append($" -t {Math.Round(Timeout.Value.TotalSeconds, 0)}");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose rm command.
  /// </summary>
  public struct ComposeRmCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Don't ask to confirm removal.</summary>
    public bool Force { get; set; }
    /// <summary>Stop the containers, if required, before removing.</summary>
    public bool Stop { get; set; }
    /// <summary>Remove any anonymous volumes attached to containers.</summary>
    public bool RemoveVolumes { get; set; }
    /// <summary>Deprecated, use --stop instead.</summary>
    public bool All { get; set; }
    /// <summary>Services to remove.</summary>
    public IList<string> Services { get; set; }
    /// <summary>Environment variables.</summary>
    public IDictionary<string, string> Env { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.Append(" -f"); // Don't ask to confirm removal
      if (Force || Stop)
        sb.Append(" -s");
      if (RemoveVolumes)
        sb.Append(" -v");
      if (All)
        sb.Append(" -a");

      if (Services != null && Services.Count > 0)
        sb.Append(" " + string.Join(" ", Services));

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose ps command.
  /// </summary>
  public struct ComposePsCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Filter services by a property.</summary>
    public string[] Filters { get; set; }
    /// <summary>Format the output.</summary>
    public string Format { get; set; }
    /// <summary>Only display IDs.</summary>
    public bool Quiet { get; set; }
    /// <summary>Display services.</summary>
    public bool Services { get; set; }
    /// <summary>Show all stopped containers.</summary>
    public bool All { get; set; }
    /// <summary>Filter services by status.</summary>
    public string[] Status { get; set; }
    /// <summary>Services to show.</summary>
    public IList<string> ServiceNames { get; set; }
    /// <summary>Environment variables.</summary>
    public IDictionary<string, string> Env { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--filter ", Filters);
      sb.OptionIfExists("--format ", Format);
      if (Quiet)
        sb.Append(" -q");
      if (Services)
        sb.Append(" --services");
      if (All)
        sb.Append(" -a");
      sb.OptionIfExists("--status ", Status);

      if (ServiceNames != null && ServiceNames.Count > 0)
        sb.Append(" " + string.Join(" ", ServiceNames));

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose port command.
  /// </summary>
  public struct ComposePortCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Service name.</summary>
    public string Service { get; set; }
    /// <summary>Private port (port/proto, e.g. 3000/tcp or 3000/udp).</summary>
    public string PrivatePort { get; set; }
    /// <summary>Index of the container if there are multiple instances (default: 1).</summary>
    public int? Index { get; set; }
    /// <summary>Protocol (tcp or udp).</summary>
    public string Protocol { get; set; }
    /// <summary>Environment variables.</summary>
    public IDictionary<string, string> Env { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Index.HasValue)
        sb.Append($" --index={Index.Value}");
      sb.OptionIfExists("--protocol ", Protocol);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose config command.
  /// </summary>
  public struct ComposeConfigCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Format the output.</summary>
    public string Format { get; set; }
    /// <summary>Pin image tags to digests.</summary>
    public bool ResolveImageDigests { get; set; }
    /// <summary>Don't interpolate environment variables.</summary>
    public bool NoInterpolate { get; set; }
    /// <summary>Only validate the configuration.</summary>
    public bool Quiet { get; set; }
    /// <summary>Print the service names.</summary>
    public bool Services { get; set; }
    /// <summary>Print the volume names.</summary>
    public bool Volumes { get; set; }
    /// <summary>Print the profile names.</summary>
    public bool Profiles { get; set; }
    /// <summary>Print the image names.</summary>
    public bool Images { get; set; }
    /// <summary>Output hash of the service configuration.</summary>
    public string Hash { get; set; }
    /// <summary>Environment variables.</summary>
    public IDictionary<string, string> Env { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--format ", Format);
      if (ResolveImageDigests)
        sb.Append(" --resolve-image-digests");
      if (NoInterpolate)
        sb.Append(" --no-interpolate");
      if (Quiet)
        sb.Append(" -q");
      if (Services)
        sb.Append(" --services");
      if (Volumes)
        sb.Append(" --volumes");
      if (Profiles)
        sb.Append(" --profiles");
      if (Images)
        sb.Append(" --images");
      sb.OptionIfExists("--hash ", Hash);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose scale command.
  /// </summary>
  public struct ComposeScaleCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Don't start linked services.</summary>
    public bool NoDeps { get; set; }
    /// <summary>Timeout in seconds for container shutdown.</summary>
    public TimeSpan? Timeout { get; set; }
    /// <summary>Service=number pairs (e.g., web=3).</summary>
    public IList<string> ServiceScales { get; set; }
    /// <summary>Environment variables.</summary>
    public IDictionary<string, string> Env { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (NoDeps)
        sb.Append(" --no-deps");
      if (Timeout.HasValue)
        sb.Append($" -t {Math.Round(Timeout.Value.TotalSeconds, 0)}");

      if (ServiceScales != null && ServiceScales.Count > 0)
        sb.Append(" " + string.Join(" ", ServiceScales));

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose version command.
  /// </summary>
  public struct ComposeVersionCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Format the output.</summary>
    public string Format { get; set; }
    /// <summary>Shows only Compose's version number.</summary>
    public bool Short { get; set; }
    /// <summary>Environment variables.</summary>
    public IDictionary<string, string> Env { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--format ", Format);
      if (Short)
        sb.Append(" --short");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose exec command.
  /// </summary>
  public struct ComposeExecCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Service name.</summary>
    public string Service { get; set; }
    /// <summary>Command to execute.</summary>
    public string Command { get; set; }
    /// <summary>Arguments to the command.</summary>
    public IList<string> Arguments { get; set; }
    /// <summary>Detached mode.</summary>
    public bool Detach { get; set; }
    /// <summary>Give extended privileges to the process.</summary>
    public bool Privileged { get; set; }
    /// <summary>Run as this user.</summary>
    public string User { get; set; }
    /// <summary>Allocate a pseudo-TTY.</summary>
    public bool Tty { get; set; }
    /// <summary>Disable pseudo-TTY allocation.</summary>
    public bool NoTty { get; set; }
    /// <summary>Working directory inside the container.</summary>
    public string WorkDir { get; set; }
    /// <summary>Set environment variables.</summary>
    public string[] Environment { get; set; }
    /// <summary>Index of the container if multiple instances.</summary>
    public int? Index { get; set; }
    /// <summary>Environment variables.</summary>
    public IDictionary<string, string> Env { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Detach)
        sb.Append(" -d");
      if (Privileged)
        sb.Append(" --privileged");
      sb.OptionIfExists("-u ", User);
      if (Tty)
        sb.Append(" -T");
      if (NoTty)
        sb.Append(" --no-TTY");
      sb.OptionIfExists("-w ", WorkDir);
      sb.OptionIfExists("-e ", Environment);
      if (Index.HasValue)
        sb.Append($" --index={Index.Value}");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose run command.
  /// </summary>
  public struct ComposeRunCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Service name.</summary>
    public string Service { get; set; }
    /// <summary>Command to execute.</summary>
    public string Command { get; set; }
    /// <summary>Arguments to the command.</summary>
    public IList<string> Arguments { get; set; }
    /// <summary>Run container in detached mode.</summary>
    public bool Detach { get; set; }
    /// <summary>Assign a name to the container.</summary>
    public string Name { get; set; }
    /// <summary>Override the entrypoint.</summary>
    public string Entrypoint { get; set; }
    /// <summary>Set environment variables.</summary>
    public string[] Environment { get; set; }
    /// <summary>Add or override a label.</summary>
    public string[] Labels { get; set; }
    /// <summary>Run as this user.</summary>
    public string User { get; set; }
    /// <summary>Don't start linked services.</summary>
    public bool NoDeps { get; set; }
    /// <summary>Automatically remove the container when it exits.</summary>
    public bool Rm { get; set; }
    /// <summary>Publish a container's port(s) to the host.</summary>
    public string[] Publish { get; set; }
    /// <summary>Run command with the service's ports enabled.</summary>
    public bool ServicePorts { get; set; }
    /// <summary>Use the service's network aliases.</summary>
    public bool UseAliases { get; set; }
    /// <summary>Bind mount a volume.</summary>
    public string[] Volumes { get; set; }
    /// <summary>Disable pseudo-TTY allocation.</summary>
    public bool NoTty { get; set; }
    /// <summary>Working directory inside the container.</summary>
    public string WorkDir { get; set; }
    /// <summary>Build image before starting container.</summary>
    public bool Build { get; set; }
    /// <summary>Pull image before running.</summary>
    public bool Pull { get; set; }
    /// <summary>Remove containers for services not defined in the Compose file.</summary>
    public bool RemoveOrphans { get; set; }
    /// <summary>Do not send input to the container.</summary>
    public bool Quiet { get; set; }
    /// <summary>Environment variables.</summary>
    public IDictionary<string, string> Env { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Detach)
        sb.Append(" -d");
      sb.OptionIfExists("--name ", Name);
      sb.OptionIfExists("--entrypoint ", Entrypoint);
      sb.OptionIfExists("-e ", Environment);
      sb.OptionIfExists("-l ", Labels);
      sb.OptionIfExists("-u ", User);
      if (NoDeps)
        sb.Append(" --no-deps");
      if (Rm)
        sb.Append(" --rm");
      sb.OptionIfExists("-p ", Publish);
      if (ServicePorts)
        sb.Append(" --service-ports");
      if (UseAliases)
        sb.Append(" --use-aliases");
      sb.OptionIfExists("-v ", Volumes);
      if (NoTty)
        sb.Append(" -T");
      sb.OptionIfExists("-w ", WorkDir);
      if (Build)
        sb.Append(" --build");
      if (Pull)
        sb.Append(" --pull");
      if (RemoveOrphans)
        sb.Append(" --remove-orphans");
      if (Quiet)
        sb.Append(" --quiet-pull");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose top command.
  /// </summary>
  public struct ComposeTopCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Services to display.</summary>
    public IList<string> Services { get; set; }
    /// <summary>Environment variables.</summary>
    public IDictionary<string, string> Env { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Services != null && Services.Count > 0)
        sb.Append(" " + string.Join(" ", Services));

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose images command.
  /// </summary>
  public struct ComposeImagesCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Format the output.</summary>
    public string Format { get; set; }
    /// <summary>Only display IDs.</summary>
    public bool Quiet { get; set; }
    /// <summary>Services to display images for.</summary>
    public IList<string> Services { get; set; }
    /// <summary>Environment variables.</summary>
    public IDictionary<string, string> Env { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--format ", Format);
      if (Quiet)
        sb.Append(" -q");

      if (Services != null && Services.Count > 0)
        sb.Append(" " + string.Join(" ", Services));

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose cp command.
  /// </summary>
  public struct ComposeCpCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Source path (container:path or host path).</summary>
    public string Source { get; set; }
    /// <summary>Destination path (container:path or host path).</summary>
    public string Destination { get; set; }
    /// <summary>Index of the container if there are multiple instances.</summary>
    public int? Index { get; set; }
    /// <summary>Archive mode (copy all uid/gid information).</summary>
    public bool Archive { get; set; }
    /// <summary>Always follow symbol link in SRC_PATH.</summary>
    public bool FollowLink { get; set; }
    /// <summary>Environment variables.</summary>
    public IDictionary<string, string> Env { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Index.HasValue)
        sb.Append($" --index={Index.Value}");
      if (Archive)
        sb.Append(" --archive");
      if (FollowLink)
        sb.Append(" --follow-link");

      return sb.ToString();
    }
  }

}

