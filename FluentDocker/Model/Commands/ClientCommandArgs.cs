using System;
using System.Collections.Generic;
using System.Text;
using FluentDocker.Extensions;
using FluentDocker.Model.Containers;

namespace FluentDocker.Model.Commands
{
  /// <summary>
  /// Arguments for docker login command.
  /// </summary>
  public struct LoginCommandArgs
  {
    /// <summary>The registry server URL.</summary>
    public string Server { get; set; }
    /// <summary>Username for authentication.</summary>
    public string User { get; set; }
    /// <summary>Password for authentication.</summary>
    public string Password { get; set; }
    /// <summary>Take the password from stdin.</summary>
    public bool PasswordStdin { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("-u ", User);
      sb.OptionIfExists("-p ", Password);
      if (PasswordStdin)
        sb.Append(" --password-stdin");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker ps command.
  /// </summary>
  public struct PsCommandArgs
  {
    /// <summary>Show all containers (default shows just running).</summary>
    public bool All { get; set; }
    /// <summary>Only display container IDs.</summary>
    public bool Quiet { get; set; }
    /// <summary>Don't truncate output.</summary>
    public bool NoTrunc { get; set; }
    /// <summary>Filter output based on conditions.</summary>
    public string[] Filters { get; set; }
    /// <summary>Format the output using a Go template.</summary>
    public string Format { get; set; }
    /// <summary>Show n last created containers.</summary>
    public int? Last { get; set; }
    /// <summary>Show the latest created container.</summary>
    public bool Latest { get; set; }
    /// <summary>Display total file sizes.</summary>
    public bool Size { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (All)
        sb.Append(" --all");
      if (Quiet)
        sb.Append(" --quiet");
      if (NoTrunc)
        sb.Append(" --no-trunc");
      sb.OptionIfExists("--filter=", Filters);
      sb.OptionIfExists("--format ", Format);
      if (Last.HasValue)
        sb.Append($" --last {Last.Value}");
      if (Latest)
        sb.Append(" --latest");
      if (Size)
        sb.Append(" --size");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker stop command.
  /// </summary>
  public struct StopCommandArgs
  {
    /// <summary>The container ID or name.</summary>
    public string ContainerId { get; set; }
    /// <summary>Seconds to wait before killing the container.</summary>
    public TimeSpan? Timeout { get; set; }
    /// <summary>Signal to send to the container.</summary>
    public string Signal { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Timeout.HasValue)
        sb.Append($" --time={Math.Round(Timeout.Value.TotalSeconds, 0)}");
      sb.OptionIfExists("--signal ", Signal);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker rm command.
  /// </summary>
  public struct RemoveContainerCommandArgs
  {
    /// <summary>The container ID or name.</summary>
    public string ContainerId { get; set; }
    /// <summary>Force removal of running container.</summary>
    public bool Force { get; set; }
    /// <summary>Remove anonymous volumes associated with the container.</summary>
    public bool RemoveVolumes { get; set; }
    /// <summary>Remove the specified link.</summary>
    public string RemoveLink { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Force)
        sb.Append(" --force");
      if (RemoveVolumes)
        sb.Append(" --volumes");
      sb.OptionIfExists("--link ", RemoveLink);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker exec command.
  /// </summary>
  public struct ExecCommandArgs
  {
    /// <summary>The container ID or name.</summary>
    public string ContainerId { get; set; }
    /// <summary>The command to execute.</summary>
    public string Command { get; set; }
    /// <summary>Arguments to the command.</summary>
    public string[] Arguments { get; set; }
    /// <summary>Keep STDIN open even if not attached.</summary>
    public bool Interactive { get; set; }
    /// <summary>Allocate a pseudo-TTY.</summary>
    public bool Tty { get; set; }
    /// <summary>Detached mode: run command in the background.</summary>
    public bool Detach { get; set; }
    /// <summary>Username or UID.</summary>
    public string User { get; set; }
    /// <summary>Working directory inside the container.</summary>
    public string WorkDir { get; set; }
    /// <summary>Set environment variables.</summary>
    public string[] Environment { get; set; }
    /// <summary>Read environment variables from a file.</summary>
    public string[] EnvFiles { get; set; }
    /// <summary>Give extended privileges to the command.</summary>
    public bool Privileged { get; set; }
    /// <summary>Override the key sequence for detaching a container.</summary>
    public string DetachKeys { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Interactive)
        sb.Append(" -i");
      if (Tty)
        sb.Append(" -t");
      if (Detach)
        sb.Append(" -d");
      sb.OptionIfExists("-u ", User);
      sb.OptionIfExists("-w ", WorkDir);
      sb.OptionIfExists("-e ", Environment);
      sb.OptionIfExists("--env-file ", EnvFiles);
      if (Privileged)
        sb.Append(" --privileged");
      sb.OptionIfExists("--detach-keys ", DetachKeys);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker export command.
  /// </summary>
  public struct ExportCommandArgs
  {
    /// <summary>The container ID or name.</summary>
    public string ContainerId { get; set; }
    /// <summary>Write to a file, instead of STDOUT.</summary>
    public string OutputFilePath { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("-o ", OutputFilePath);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker cp command.
  /// </summary>
  public struct CopyCommandArgs
  {
    /// <summary>The container ID or name.</summary>
    public string ContainerId { get; set; }
    /// <summary>Path inside the container.</summary>
    public string ContainerPath { get; set; }
    /// <summary>Path on the host.</summary>
    public string HostPath { get; set; }
    /// <summary>Archive mode (copy all uid/gid information).</summary>
    public bool Archive { get; set; }
    /// <summary>Follow symbolic links in source path.</summary>
    public bool FollowLink { get; set; }
    /// <summary>Suppress progress output.</summary>
    public bool Quiet { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Archive)
        sb.Append(" -a");
      if (FollowLink)
        sb.Append(" -L");
      if (Quiet)
        sb.Append(" -q");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker pull command.
  /// </summary>
  public struct PullCommandArgs
  {
    /// <summary>The image name to pull.</summary>
    public string Image { get; set; }
    /// <summary>Download all tagged images in the repository.</summary>
    public bool AllTags { get; set; }
    /// <summary>Skip image verification.</summary>
    public bool DisableContentTrust { get; set; }
    /// <summary>Set platform if server is multi-platform capable.</summary>
    public string Platform { get; set; }
    /// <summary>Suppress verbose output.</summary>
    public bool Quiet { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (AllTags)
        sb.Append(" --all-tags");
      if (DisableContentTrust)
        sb.Append(" --disable-content-trust");
      sb.OptionIfExists("--platform ", Platform);
      if (Quiet)
        sb.Append(" --quiet");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker build command.
  /// </summary>
  public struct BuildCommandArgs
  {
    /// <summary>Name and optionally a tag (name:tag).</summary>
    public string Tag { get; set; }
    /// <summary>Name of the Dockerfile.</summary>
    public string File { get; set; }
    /// <summary>Build context path or URL.</summary>
    public string Context { get; set; }
    /// <summary>Set build-time variables.</summary>
    public string[] BuildArgs { get; set; }
    /// <summary>Images to consider as cache sources.</summary>
    public string[] CacheFrom { get; set; }
    /// <summary>Skip image verification.</summary>
    public bool DisableContentTrust { get; set; }
    /// <summary>Always remove intermediate containers.</summary>
    public bool ForceRm { get; set; }
    /// <summary>Set metadata for an image.</summary>
    public string[] Labels { get; set; }
    /// <summary>Do not use cache when building the image.</summary>
    public bool NoCache { get; set; }
    /// <summary>Always attempt to pull a newer version of the image.</summary>
    public bool Pull { get; set; }
    /// <summary>Suppress the build output and print image ID on success.</summary>
    public bool Quiet { get; set; }
    /// <summary>Remove intermediate containers after a successful build.</summary>
    public bool Rm { get; set; }
    /// <summary>Set target build stage to build.</summary>
    public string Target { get; set; }
    /// <summary>Memory limit.</summary>
    public string Memory { get; set; }
    /// <summary>Swap limit equal to memory plus swap.</summary>
    public string MemorySwap { get; set; }
    /// <summary>CPU shares (relative weight).</summary>
    public float? CpuShares { get; set; }
    /// <summary>CPUs in which to allow execution.</summary>
    public string CpusetCpus { get; set; }
    /// <summary>Size of /dev/shm.</summary>
    public string ShmSize { get; set; }
    /// <summary>Squash newly built layers into a single new layer.</summary>
    public bool Squash { get; set; }
    /// <summary>Add a host file network.</summary>
    public string[] AddHosts { get; set; }
    /// <summary>Set the networking mode for the build.</summary>
    public string Network { get; set; }
    /// <summary>Set the platform for the build.</summary>
    public string Platform { get; set; }
    /// <summary>Secret to expose to the build.</summary>
    public string[] Secrets { get; set; }
    /// <summary>SSH agent socket or keys to expose to the build.</summary>
    public string[] Ssh { get; set; }
    /// <summary>Output destination.</summary>
    public string Output { get; set; }
    /// <summary>Set type of progress output (auto, plain, tty).</summary>
    public string Progress { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("-t ", Tag);
      sb.OptionIfExists("-f ", File);
      sb.OptionIfExists("--build-arg ", BuildArgs);
      sb.OptionIfExists("--cache-from ", CacheFrom);
      if (DisableContentTrust)
        sb.Append(" --disable-content-trust");
      if (ForceRm)
        sb.Append(" --force-rm");
      sb.OptionIfExists("--label ", Labels);
      if (NoCache)
        sb.Append(" --no-cache");
      if (Pull)
        sb.Append(" --pull");
      if (Quiet)
        sb.Append(" --quiet");
      if (Rm)
        sb.Append(" --rm");
      sb.OptionIfExists("--target ", Target);
      sb.OptionIfExists("--memory ", Memory);
      sb.OptionIfExists("--memory-swap ", MemorySwap);
      if (CpuShares.HasValue)
        sb.Append($" --cpu-shares {CpuShares.Value}");
      sb.OptionIfExists("--cpuset-cpus ", CpusetCpus);
      sb.OptionIfExists("--shm-size ", ShmSize);
      if (Squash)
        sb.Append(" --squash");
      sb.OptionIfExists("--add-host ", AddHosts);
      sb.OptionIfExists("--network ", Network);
      sb.OptionIfExists("--platform ", Platform);
      sb.OptionIfExists("--secret ", Secrets);
      sb.OptionIfExists("--ssh ", Ssh);
      sb.OptionIfExists("--output ", Output);
      sb.OptionIfExists("--progress ", Progress);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker run command.
  /// </summary>
  public struct RunCommandArgs
  {
    /// <summary>The image to run.</summary>
    public string Image { get; set; }
    /// <summary>The command to run.</summary>
    public string Command { get; set; }
    /// <summary>Arguments to the command.</summary>
    public string[] Arguments { get; set; }
    /// <summary>Run container in detached mode.</summary>
    public bool Detach { get; set; }
    /// <summary>Container create parameters.</summary>
    public ContainerCreateParams CreateParams { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Detach)
        sb.Append(" -d");
      if (CreateParams != null)
        sb.Append(CreateParams.ToString());

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker create command.
  /// </summary>
  public struct CreateCommandArgs
  {
    /// <summary>The image to create container from.</summary>
    public string Image { get; set; }
    /// <summary>The command to run.</summary>
    public string Command { get; set; }
    /// <summary>Arguments to the command.</summary>
    public string[] Arguments { get; set; }
    /// <summary>Container create parameters.</summary>
    public ContainerCreateParams CreateParams { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (CreateParams != null)
        sb.Append(CreateParams.ToString());

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker start command.
  /// </summary>
  public struct StartCommandArgs
  {
    /// <summary>The container IDs or names.</summary>
    public IList<string> ContainerIds { get; set; }
    /// <summary>Attach STDOUT/STDERR and forward signals.</summary>
    public bool Attach { get; set; }
    /// <summary>Attach container's STDIN.</summary>
    public bool Interactive { get; set; }
    /// <summary>Override the key sequence for detaching a container.</summary>
    public string DetachKeys { get; set; }
    /// <summary>Checkpoint to restore from.</summary>
    public string Checkpoint { get; set; }
    /// <summary>Use a custom checkpoint storage directory.</summary>
    public string CheckpointDir { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Attach)
        sb.Append(" -a");
      if (Interactive)
        sb.Append(" -i");
      sb.OptionIfExists("--detach-keys ", DetachKeys);
      sb.OptionIfExists("--checkpoint ", Checkpoint);
      sb.OptionIfExists("--checkpoint-dir ", CheckpointDir);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker inspect command.
  /// </summary>
  public struct InspectCommandArgs
  {
    /// <summary>The container/image IDs or names.</summary>
    public IList<string> Ids { get; set; }
    /// <summary>Return JSON for specified type.</summary>
    public string Type { get; set; }
    /// <summary>Format the output using a Go template.</summary>
    public string Format { get; set; }
    /// <summary>Display total file sizes if the type is container.</summary>
    public bool Size { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--type ", Type);
      sb.OptionIfExists("--format ", Format);
      if (Size)
        sb.Append(" --size");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker top command.
  /// </summary>
  public struct TopCommandArgs
  {
    /// <summary>The container ID or name.</summary>
    public string ContainerId { get; set; }
    /// <summary>ps options.</summary>
    public string PsOptions { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
  }

  /// <summary>
  /// Arguments for docker diff command.
  /// </summary>
  public struct DiffCommandArgs
  {
    /// <summary>The container ID or name.</summary>
    public string ContainerId { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
  }

  /// <summary>
  /// Arguments for docker pause command.
  /// </summary>
  public struct PauseCommandArgs
  {
    /// <summary>The container IDs or names.</summary>
    public IList<string> ContainerIds { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
  }

  /// <summary>
  /// Arguments for docker unpause command.
  /// </summary>
  public struct UnpauseCommandArgs
  {
    /// <summary>The container IDs or names.</summary>
    public IList<string> ContainerIds { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
  }

  /// <summary>
  /// Arguments for docker kill command.
  /// </summary>
  public struct KillCommandArgs
  {
    /// <summary>The container IDs or names.</summary>
    public IList<string> ContainerIds { get; set; }
    /// <summary>Signal to send to the container.</summary>
    public string Signal { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--signal ", Signal);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker restart command.
  /// </summary>
  public struct RestartCommandArgs
  {
    /// <summary>The container IDs or names.</summary>
    public IList<string> ContainerIds { get; set; }
    /// <summary>Seconds to wait before killing the container.</summary>
    public TimeSpan? Timeout { get; set; }
    /// <summary>Signal to send to the container.</summary>
    public string Signal { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Timeout.HasValue)
        sb.Append($" --time={Math.Round(Timeout.Value.TotalSeconds, 0)}");
      sb.OptionIfExists("--signal ", Signal);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker wait command.
  /// </summary>
  public struct WaitCommandArgs
  {
    /// <summary>The container IDs or names.</summary>
    public IList<string> ContainerIds { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
  }

  /// <summary>
  /// Arguments for docker rename command.
  /// </summary>
  public struct RenameCommandArgs
  {
    /// <summary>Current container name.</summary>
    public string OldName { get; set; }
    /// <summary>New container name.</summary>
    public string NewName { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
  }

  /// <summary>
  /// Arguments for docker attach command.
  /// </summary>
  public struct AttachCommandArgs
  {
    /// <summary>The container ID or name.</summary>
    public string ContainerId { get; set; }
    /// <summary>Override the key sequence for detaching.</summary>
    public string DetachKeys { get; set; }
    /// <summary>Do not attach STDIN.</summary>
    public bool NoStdin { get; set; }
    /// <summary>Proxy all received signals to the process.</summary>
    public bool SigProxy { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--detach-keys ", DetachKeys);
      if (NoStdin)
        sb.Append(" --no-stdin");
      if (SigProxy)
        sb.Append(" --sig-proxy");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker commit command.
  /// </summary>
  public struct CommitCommandArgs
  {
    /// <summary>The container ID or name.</summary>
    public string ContainerId { get; set; }
    /// <summary>Repository name for the new image.</summary>
    public string Repository { get; set; }
    /// <summary>Tag for the new image.</summary>
    public string Tag { get; set; }
    /// <summary>Author.</summary>
    public string Author { get; set; }
    /// <summary>Apply Dockerfile instruction to the created image.</summary>
    public string[] Changes { get; set; }
    /// <summary>Commit message.</summary>
    public string Message { get; set; }
    /// <summary>Pause container during commit.</summary>
    public bool Pause { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--author ", Author);
      sb.OptionIfExists("--change ", Changes);
      sb.OptionIfExists("--message ", Message);
      if (Pause)
        sb.Append(" --pause");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker stats command.
  /// </summary>
  public struct StatsCommandArgs
  {
    /// <summary>The container IDs or names.</summary>
    public IList<string> ContainerIds { get; set; }
    /// <summary>Show all containers (default shows just running).</summary>
    public bool All { get; set; }
    /// <summary>Pretty-print images using a Go template.</summary>
    public string Format { get; set; }
    /// <summary>Disable streaming stats and only pull the first result.</summary>
    public bool NoStream { get; set; }
    /// <summary>Do not truncate output.</summary>
    public bool NoTrunc { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (All)
        sb.Append(" --all");
      sb.OptionIfExists("--format ", Format);
      if (NoStream)
        sb.Append(" --no-stream");
      if (NoTrunc)
        sb.Append(" --no-trunc");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker update command.
  /// </summary>
  public struct UpdateCommandArgs
  {
    /// <summary>The container IDs or names.</summary>
    public IList<string> ContainerIds { get; set; }
    /// <summary>Block IO weight (10-1000).</summary>
    public int? BlkioWeight { get; set; }
    /// <summary>CPU shares (relative weight).</summary>
    public int? CpuShares { get; set; }
    /// <summary>Limit CPU CFS period.</summary>
    public long? CpuPeriod { get; set; }
    /// <summary>Limit CPU CFS quota.</summary>
    public long? CpuQuota { get; set; }
    /// <summary>Limit the CPU real-time period.</summary>
    public long? CpuRtPeriod { get; set; }
    /// <summary>Limit the CPU real-time runtime.</summary>
    public long? CpuRtRuntime { get; set; }
    /// <summary>CPUs in which to allow execution.</summary>
    public string CpusetCpus { get; set; }
    /// <summary>MEMs in which to allow execution.</summary>
    public string CpusetMems { get; set; }
    /// <summary>Memory limit.</summary>
    public string Memory { get; set; }
    /// <summary>Memory soft limit.</summary>
    public string MemoryReservation { get; set; }
    /// <summary>Swap limit equal to memory plus swap.</summary>
    public string MemorySwap { get; set; }
    /// <summary>Kernel memory limit.</summary>
    public string KernelMemory { get; set; }
    /// <summary>Restart policy.</summary>
    public string RestartPolicy { get; set; }
    /// <summary>Amount of CPUs.</summary>
    public float? Cpus { get; set; }
    /// <summary>Container PIDs limit.</summary>
    public long? PidsLimit { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (BlkioWeight.HasValue)
        sb.Append($" --blkio-weight {BlkioWeight.Value}");
      if (CpuShares.HasValue)
        sb.Append($" --cpu-shares {CpuShares.Value}");
      if (CpuPeriod.HasValue)
        sb.Append($" --cpu-period {CpuPeriod.Value}");
      if (CpuQuota.HasValue)
        sb.Append($" --cpu-quota {CpuQuota.Value}");
      if (CpuRtPeriod.HasValue)
        sb.Append($" --cpu-rt-period {CpuRtPeriod.Value}");
      if (CpuRtRuntime.HasValue)
        sb.Append($" --cpu-rt-runtime {CpuRtRuntime.Value}");
      sb.OptionIfExists("--cpuset-cpus ", CpusetCpus);
      sb.OptionIfExists("--cpuset-mems ", CpusetMems);
      sb.OptionIfExists("--memory ", Memory);
      sb.OptionIfExists("--memory-reservation ", MemoryReservation);
      sb.OptionIfExists("--memory-swap ", MemorySwap);
      sb.OptionIfExists("--kernel-memory ", KernelMemory);
      sb.OptionIfExists("--restart ", RestartPolicy);
      if (Cpus.HasValue)
        sb.Append($" --cpus {Cpus.Value}");
      if (PidsLimit.HasValue)
        sb.Append($" --pids-limit {PidsLimit.Value}");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker port command.
  /// </summary>
  public struct PortCommandArgs
  {
    /// <summary>The container ID or name.</summary>
    public string ContainerId { get; set; }
    /// <summary>The private port (format: port[/proto]).</summary>
    public string PrivatePort { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
  }
}

