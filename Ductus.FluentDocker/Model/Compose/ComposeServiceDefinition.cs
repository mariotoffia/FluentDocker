using System.Collections.Generic;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Model.Compose
{
  // https://docs.docker.com/compose/compose-file/#service-configuration-reference
  /// <summary>
  /// 
  /// </summary>
  /// <remarks>
  /// The following sub-options (supported for docker-compose up and docker-compose run) are not supported for docker stack deploy or the deploy key.
  /// build
  /// cgroup_parent
  /// container_name
  /// devices
  /// tmpfs
  /// external_links
  /// links
  /// network_mode
  /// restart
  /// security_opt
  /// stop_signal
  /// sysctls
  /// userns_mode
  /// </remarks>
  public sealed class ComposeServiceDefinition
  {
    public string Name { get; set; }
    public string Image { get; set; }
    public BuildDefinition Build { get; set; }
    public IList<string> Volumes { get; } = new List<string>();
    public RestartPolicy Restart { get; set; }
    public IDictionary<string, string> Environment { get; set; }
    public IList<string> Ports { get; set; } = new List<string>();
    /// <summary>
    /// Add container capabilities. See man 7 capabilities for a full list.
    /// </summary>
    /// <remarks>
    /// Note: These options are ignored when deploying a stack in swarm mode with a (version 3) Compose file.
    /// cap_add:
    ///   - ALL
    /// </remarks>
    public IList<string> CapAdd { get; set; } = new List<string>();
    /// <summary>
    /// Drop container capabilities. See man 7 capabilities for a full list.
    /// </summary>
    /// <remarks>
    /// Note: These options are ignored when deploying a stack in swarm mode with a (version 3) Compose file.
    /// cap_drop:
    ///   - NET_ADMIN
    ///   - SYS_ADMIN
    /// </remarks>
    public IList<string> CapDrop { get; set; } = new List<string>();
    /// <summary>
    /// Override the default command.
    /// </summary>
    /// <remarks>
    /// For example:
    /// command: bundle exec thin -p 3000
    /// command: ["bundle", "exec", "thin", "-p", "3000"]
    /// </remarks>
    public string Command { get; set; }
    /// <summary>
    /// Grant access to configs on a per-service basis using the per-service configs configuration. 
    /// </summary>
    /// <remarks>
    /// Note: config definitions are only supported in version 3.3 and higher of the compose file format.
    /// Note: The config must already exist or be defined in the top-level configs configuration of this stack file,
    /// or stack deployment fails.
    /// </remarks>
    public IList<string> ConfigsShort { get; set; }
    /// <summary>
    /// The long syntax provides more granularity in how the config is created within the service’s task containers.
    /// </summary>
    /// <remarks>
    /// Note: config definitions are only supported in version 3.3 and higher of the compose file format.
    /// </remarks>
    public IList<ConfigLongDefinition> ConfigLong { get; set; } = new List<ConfigLongDefinition>();

    /// <summary>
    /// Specify an optional parent cgroup for the container.
    /// </summary>
    /// <remarks>
    /// Note: This option is ignored when deploying a stack in swarm mode with a (version 3) Compose file.
    /// For example cgroup_parent: m-executor-abcd
    /// </remarks>
    public string CgroupParent { get; set; }
    /// <summary>
    /// Specify a custom container name, rather than a generated default name.
    /// </summary>
    /// <remarks>
    /// Note: This option is ignored when deploying a stack in swarm mode with a (version 3) Compose file.
    /// Because Docker container names must be unique, you cannot scale a service beyond 1 container if you
    /// have specified a custom name. Attempting to do so results in an error.
    /// For example container_name: my-web-container
    /// </remarks>
    public string ContainerName { get; set; }    
    /// <summary>
    /// Configure the credential spec for managed service account.
    /// </summary>
    /// <remarks>
    /// Note: this option was added in v3.3.
    /// his option is only used for services using Windows containers. The credential_spec must be in the format
    /// file://"filename" or registry://"value-name". When using file:, the referenced file must be present in the
    /// CredentialSpecs subdirectory in the docker data directory, which defaults to C:\ProgramData\Docker\ on Windows.
    /// The following example loads the credential spec from a file named
    /// C:\ProgramData\Docker\CredentialSpecs\my-credential-spec.json:
    /// credential_spec:
    ///   file: my-credential-spec.json
    /// When using registry:, the credential spec is read from the Windows registry on the daemon’s host. A registry
    /// value with the given name must be located in:
    /// HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization\Containers\CredentialSpecs
    /// The following example load the credential spec from a value named my-credential-spec in the registry:
    /// credential_spec:
    ///   registry: my-credential-spec
    /// </remarks>
    public string CredentialSpec { get; set; }
    /// <summary>
    /// List of device mappings. Uses the same format as the --device docker client create option.
    /// </summary>
    /// <remarks>
    /// Note: This option is ignored when deploying a stack in swarm mode with a (version 3) Compose file.
    /// For example:
    /// devices:
    ///   - "/dev/ttyUSB0:/dev/ttyUSB0"
    /// </remarks>
    public IList<string> Devices { get; set; } = new List<string>();
    /// <summary>
    /// Express dependency between services.
    /// </summary>
    /// <remarks>
    /// Service dependencies cause the following behaviors:
    /// docker-compose up starts services in dependency order. In the following example, db and redis are started
    /// before web.
    /// docker-compose up SERVICE automatically includes SERVICE’s dependencies. In the following example,
    /// docker-compose up web also creates and starts db and redis.
    /// Simple example:
    /// web:
    ///   build: .
    ///   depends_on:
    ///     - db
    ///     - redis
    /// redis:
    ///   image: redis
    /// db:
    ///   image: postgres
    /// </remarks>
    public IList<string> DependsOn { get; set; } = new List<string>();
    /// <summary>
    /// Custom dns entries.
    /// </summary>
    public IList<string> Dns { get; set; } = new List<string>();
    /// <summary>
    /// Custom DNS search domains.
    /// </summary>
    /// <remarks>
    /// For example dc1.example.com, dc2.example.com.
    /// </remarks>
    public IList<string> DnsSearch { get; set; } = new List<string>();
    /// <summary>
    /// Mount a temporary file system inside the container.
    /// </summary>
    /// <remarks>
    /// Note: Version 2 file format and up.
    /// Note: This option is ignored when deploying a stack in swarm mode with a (version 3-3.5) Compose file.
    /// For example /run, /tmp
    /// If specify more than <see cref="TmpFsDefinition.Target"/> it requires that the version is 3.6 or more.
    /// </remarks>
    public IList<TmpFsDefinition> TmpFs { get; set; } = new List<TmpFsDefinition>();
  }
}