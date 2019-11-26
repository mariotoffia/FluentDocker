using System.Collections.Generic;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;

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
    public BuildDefinition Build { get; set; }
    public RestartPolicy Restart { get; set; }
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
    /// <summary>
    /// Override the default entrypoint.
    /// </summary>
    /// <remarks>
    ///  Note: Setting entrypoint both overrides any default entrypoint set on the service’s image with the ENTRYPOINT
    ///  Dockerfile instruction, and clears out any default command on the image - meaning that if there’s a CMD
    ///  instruction in the Dockerfile, it is ignored.
    /// </remarks>
    public IList<string> EntryPoint { get; set; } = new List<string>();
    /// <summary>
    /// Add environment variables from a file. Can be a single value or a list.
    /// </summary>
    /// <remarks>
    ///  If you have specified a Compose file with docker-compose -f FILE, paths in env_file are relative to the
    /// directory that file is in. Environment variables declared in the environment section override these values.
    /// This holds true even if those values are empty or undefined.
    /// env_file: .env
    /// 
    /// env_file:
    /// - ./common.env
    /// - ./apps/web.env
    /// - /opt/secrets.env
    ///
    /// Compose expects each line in an env file to be in VAR=VAL format. Lines beginning with # are treated as comments
    /// and are ignored. Blank lines are also ignored.
    ///
    /// Note: If your service specifies a build option, variables defined in environment files are not automatically
    /// visible during the build. Use the args sub-option of build to define build-time environment variables.
    ///
    /// The value of VAL is used as is and not modified at all. For example if the value is surrounded by quotes
    /// (as is often the case of shell variables), the quotes are included in the value passed to Compose. Keep in mind
    /// that the order of files in the list is significant in determining the value assigned to a variable that shows
    /// up more than once. The files in the list are processed from the top down. For the same variable specified in
    /// file a.env and assigned a different value in file b.env, if b.env is listed below (after), then the value from
    /// b.env stands.
    /// </remarks>
    public IList<string> EnvFiles { get; set; } = new List<string>();
    /// <summary>
    /// Add environment variables.
    /// </summary>
    /// <remarks>
    /// You can use either an array or a dictionary. Any boolean values; true, false, yes no, need to be enclosed in
    /// quotes to ensure they are not converted to True or False by the YML parser. Environment variables with only a
    /// key are resolved to their values on the machine Compose is running on, which can be helpful for secret or
    /// host-specific values.
    /// environment:
    ///   RACK_ENV: development
    ///   SHOW: 'true'
    ///   SESSION_SECRET:
    ///
    /// Note: If your service specifies a build option, variables defined in environment are not automatically visible
    /// during the build. Use the args sub-option of build to define build-time environment variables.
    /// </remarks>
    public IDictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();
    /// <summary>
    /// Expose ports without publishing them to the host machine - they’ll only be accessible to linked services.
    /// </summary>
    /// <remarks>
    ///  Only the internal port can be specified.
    /// </remarks>
    public IList<string> ExposePorts { get; set; } = new List<string>();
    /// <summary>
    /// Link to containers started outside this docker-compose.yml or even outside of Compose.
    /// </summary>
    /// <remarks>
    /// Notes:
    /// If you’re using the version 2 or above file format, the externally-created containers must be connected to at
    /// least one of the same networks as the service that is linking to them. Links are a legacy option. We recommend
    /// using networks instead. This option is ignored when deploying a stack in swarm mode with a (version 3) Compose file.
    /// 
    /// Especially for containers that provide shared or common services. external_links follow semantics similar to
    /// the legacy option links when specifying both the container name and the link alias (CONTAINER:ALIAS).
    /// external_links:
    /// - redis_1
    /// - project_db_1:mysql
    /// - project_db_1:postgresql
    /// </remarks>
    public IList<string> ExternalLinks { get; set; } = new List<string>();
    /// <summary>
    /// Add hostname mappings.
    /// </summary>
    /// <remarks>
    /// Use the same values as the docker client --add-host parameter.
    /// extra_hosts:
    /// - "somehost:162.242.195.82"
    /// - "otherhost:50.31.209.229"
    /// An entry with the ip address and hostname is created in /etc/hosts inside containers for this service, e.g:
    /// 162.242.195.82  somehost
    /// 50.31.209.229   otherhost
    /// </remarks>
    public IDictionary<string, string> ExtraHosts { get; set; } = new Dictionary<string, string>();
    /// <summary>
    /// Health check if any.
    /// </summary>
    public HealthCheckDefinition HealthCheck { get; set; }
    /// <summary>
    /// Specify the image to start the container from.
    /// </summary>
    /// <remarks>
    /// Can either be a repository/tag or a partial image ID.
    /// image: redis
    /// image: ubuntu:14.04
    /// image: tutum/influxdb
    /// image: example-registry.com:4000/postgresql
    /// image: a4bc65fd
    /// If the image does not exist, Compose attempts to pull it, unless you have also specified build, in which case
    /// it builds it using the specified options and tags it with the specified tag.
    /// </remarks>
    public string Image { get; set; }
    /// <summary>
    /// Run an init inside the container that forwards signals and reaps processes. 
    /// </summary>
    /// <remarks>
    /// Note: Added in version 3.7 file format.
    /// Either set a boolean value to use the default init, or specify a path to a custom one.
    /// version: '3.7'
    /// services:
    /// web:
    /// image: alpine:latest
    ///   init: true
    /// 
    /// version: '2.2'
    /// services:
    /// web:
    /// image: alpine:latest
    ///   init: /usr/libexec/docker-init
    /// </remarks>
    public string Init { get; set; }
    /// <summary>
    /// Specify a container’s isolation technology.
    /// </summary>
    /// <remarks>
    ///  On Linux, the only supported value is default. On Windows, acceptable values are default, process and hyperv.
    /// </remarks>
    public ContainerIsolationType Isolation { get; set; }
    /// <summary>
    ///  Adds metadata to containers.
    /// </summary>
    /// <remarks>
    /// It’s recommended that you use reverse-DNS notation to prevent your labels from conflicting with those used by other software.
    /// labels:
    /// com.example.description: "Accounting webapp"
    /// com.example.department: "Finance"
    /// com.example.label-with-empty-value: ""
    /// 
    /// labels:
    /// - "com.example.description=Accounting webapp"
    /// - "com.example.department=Finance"
    /// - "com.example.label-with-empty-value"
    /// </remarks>
    public IDictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
    /// <summary>
    /// Specifies logging for the service.
    /// </summary>
    public LoggingDefinition Logging { get; set; }
    /// <summary>
    /// Network mode. Use the same values as the docker client --network parameter, plus the special form
    /// service:[service name].
    /// </summary>
    /// <remarks>
    /// Valid options are: bridge, host, none, service:[service name], container:[container name/id].
    /// Note: This option is ignored when deploying a stack in swarm mode with a (version 3) Compose file.
    /// network_mode: "host" cannot be mixed with links.
    /// </remarks>
    public string NetworkMode { get; set; }
    /// <summary>
    /// Networks to join, referencing entries under the top-level networks key. It also support alias function and
    /// other functionality.
    /// </summary>
    public IList<ServiceNetworkDefinition> Networks { get; set; } = new List<ServiceNetworkDefinition>();
    /// <summary>
    /// Sets the PID mode to the host PID mode.
    /// </summary>
    /// <remarks>
    /// This turns on sharing between container and the host operating system the PID address space. Containers
    /// launched with this flag can access and manipulate other containers in the bare-metal machine’s namespace and
    /// vice versa. Default is false.
    /// </remarks>
    public bool PidModeHost { get; set; }
    /// <summary>
    /// Expose ports to outside container.
    /// </summary>
    public IList<IPortsDefinition> Ports { get; set; } = new List<IPortsDefinition>();
    /// <summary>
    /// Service secret. 
    /// </summary>
    /// <remarks>
    /// Note that it has to be defined and named globally in order to reference the secret from the service.
    /// </remarks>
    public IList<ISecret> Secrets { get; set; } = new List<ISecret>();
    /// <summary>
    /// Override the default labeling scheme for each container.
    /// </summary>
    /// <remarks>
    /// Note: This option is ignored when deploying a stack in swarm mode with a (version 3) Compose file.
    /// </remarks>
    /// <example>
    /// security_opt:
    ///   - label:user:USER
    ///   - label:role:ROLE
    /// </example>
    public IList<string> SecurityOpt { get; set; } = new List<string>();

    /// <summary>
    /// Specify how long to wait when attempting to stop a container.
    /// </summary>
    /// <remarks>
    /// Specify how long to wait when attempting to stop a container if it doesn’t handle SIGTERM (or whatever stop
    /// signal has been specified with stop_signal), before sending SIGKILL. Specified as a duration. By default, stop
    /// waits 10 seconds for the container to exit before sending SIGKILL.
    /// </remarks>
    public string StopGracePeriod { get; set; }
    /// <summary>
    /// Sets an alternative signal to stop the container.
    /// </summary>
    /// <remarks>
    /// By default stop uses SIGTERM. Setting an alternative signal using stop_signal causes stop to send that signal
    /// instead. Note: This option is ignored when deploying a stack in swarm mode with a (version 3) Compose file.
    /// </remarks>
    public string StopSignal { get; set; }
    /// <summary>
    /// Kernel parameters to set in the container.
    /// </summary>
    /// <remarks>
    /// Note: This option is ignored when deploying a stack in swarm mode with a (version 3) Compose file.
    /// </remarks>
    /// <example>
    /// sysctls:
    ///   net.core.somaxconn: 1024
    ///   net.ipv4.tcp_syncookies: 0
    /// </example>
    public IDictionary<string, string> SysCtls { get; set; }
    /// <summary>
    /// Override the default ulimits for a container.
    /// </summary>
    public IDictionary<string, UlimitDefinition> Ulimits { get; set; }
    /// <summary>
    /// Disables the user namespace for this service, if Docker daemon is configured with user namespaces.
    /// </summary>
    /// <remarks>
    /// Note: This option is ignored when deploying a stack in swarm mode with a (version 3) Compose file.
    /// </remarks>
    /// <example>
    /// userns_mode: "host"
    /// </example>
    public bool DisableUserNamespaceMode { get; set; }
    /// <summary>
    /// Volume defined in this service.
    /// </summary>
    public IList<IServiceVolumeDefinition> Volumes { get; } = new List<IServiceVolumeDefinition>();
  }
}
