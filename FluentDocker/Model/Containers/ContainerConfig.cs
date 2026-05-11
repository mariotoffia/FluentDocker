using System.Collections.Generic;

namespace FluentDocker.Model.Containers
{
  /// <summary>
  /// Container configuration as returned by Docker/Podman inspect.
  /// Maps to the "Config" section of the container inspect JSON.
  /// </summary>
  public sealed class ContainerConfig
  {
    /// <summary>Hostname assigned to the container.</summary>
    public string Hostname { get; set; }


    /// <summary>Domain name for the container.</summary>
    public string DomainName { get; set; }

    /// <summary>User (UID or name) that the container runs as.</summary>
    public string User { get; set; }

    /// <summary>Whether stdin is attached to the container.</summary>
    public bool AttachStdin { get; set; }

    /// <summary>Whether stdout is attached to the container.</summary>
    public bool AttachStdout { get; set; }

    /// <summary>Whether stderr is attached to the container.</summary>
    public bool AttachStderr { get; set; }

    /// <summary>Ports exposed by the container image. Keys are "port/proto" (e.g., "80/tcp").</summary>
    public IDictionary<string /*port/proto*/, object> ExposedPorts { get; set; }

    /// <summary>Whether a pseudo-TTY is allocated.</summary>
    public bool Tty { get; set; }

    /// <summary>Whether stdin is kept open even after the client disconnects.</summary>
    public bool OpenStdin { get; set; }

    /// <summary>Whether stdin is closed after the first client detaches.</summary>
    public bool StdinOnce { get; set; }

    /// <summary>Environment variables in "KEY=VALUE" format.</summary>
    public string[] Env { get; set; }

    /// <summary>Command to run when the container starts.</summary>
    public string[] Cmd { get; set; }

    /// <summary>Name of the image used to create the container.</summary>
    public string Image { get; set; }

    /// <summary>Volume mount points defined in the image or at container creation.</summary>
    public IDictionary<string, VolumeMount> Volumes { get; set; }

    /// <summary>Working directory for commands run inside the container.</summary>
    public string WorkingDir { get; set; }

    /// <summary>Entry point for the container (overrides image ENTRYPOINT).</summary>
    public string[] EntryPoint { get; set; }

    /// <summary>User-defined key/value metadata attached to the container.</summary>
    public IDictionary<string, string> Labels { get; set; }

    /// <summary>Signal used to stop the container (e.g., "SIGTERM").</summary>
    public string StopSignal { get; set; }
  }
#pragma warning restore CA1708
}
