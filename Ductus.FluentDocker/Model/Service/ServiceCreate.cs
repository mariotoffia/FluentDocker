using System.Text;
using Ductus.FluentDocker.Extensions;

namespace Ductus.FluentDocker.Model.Service
{
  public sealed class ServiceCreate
  {
    /// <summary>
    ///   Detaches immediately when starting up.
    /// </summary>
    /// <remarks>
    ///   -d, --detach
    /// </remarks>
    public bool Detach { get; set; }

    /// <summary>
    ///   Your container will use the same DNS servers as the host by default, but you can override this with --dns.
    /// </summary>
    /// <remarks>
    ///   The IP address of a DNS server. To specify multiple DNS servers, use multiple --dns flags. If
    ///   the container cannot reach any of the IP addresses you specify, Google’s public DNS server
    ///   8.8.8.8 is added, so that your container can resolve internet domains.
    ///   --dns=[]
    /// </remarks>
    public string[] Dns { get; set; }

    /// <summary>
    ///   A key-value pair representing a DNS option and its value.
    /// </summary>
    /// <remarks>
    ///   See your operating system’s documentation for resolv.conf for valid options.
    ///   --dns-opt=[]
    /// </remarks>
    public string[] DnsOpt { get; set; }

    /// <summary>
    ///   A DNS search domain to search non-fully-qualified hostnames.
    /// </summary>
    /// <remarks>
    ///   It is possible to specify multiple DNS search prefixes.
    ///   --dns-search=[]
    /// </remarks>
    public string[] DnsSearch { get; set; }

    /// <summary>
    ///   The hostname a container uses for itself.
    /// </summary>
    /// <remarks>
    ///   Defaults to the container’s name if not specified.
    ///   --hostname
    /// </remarks>
    public string Hostname { get; set; }

    /// <summary>
    ///   Command to run the health check.
    /// </summary>
    /// <remarks>
    ///   --health-cmd
    /// </remarks>
    public string HealthCheckCmd { get; set; }

    /// <summary>
    ///   The duration between <see cref="HealthCheckCmd" /> in
    ///   (ms|s|m|h).
    /// </summary>
    /// <remarks>
    ///   --health-interval
    /// </remarks>
    public string HealthCheckInterval { get; set; }

    /// <summary>
    ///   Number of retries before it is marked as unhealthy.
    /// </summary>
    /// <remarks>
    ///   --health-retries
    /// </remarks>
    public int HealthCheckRetries { get; set; }

    /// <summary>
    ///   Start period for the container to initialize before counting retries towards unstable (ms|s|m|h).
    /// </summary>
    /// <remarks>
    ///   --health-start-period
    /// </remarks>
    public string HealthCheckInitialPeriod { get; set; }

    /// <summary>
    ///   Maximum time to allow one check to run (ms|s|m|h)
    /// </summary>
    /// <remarks>
    ///   --health-timeout
    /// </remarks>
    public string HealthCheckCmdTimeout { get; set; }

    /// <summary>
    ///   Disable any container-specified health check.
    /// </summary>
    /// <remarks>
    ///   --no-healthcheck
    /// </remarks>
    public bool? HealthCheckDisabled { get; set; }
    
    /// <summary>
    /// Overwrite the default ENTRYPOINT of the image
    /// </summary>
    /// <remarks>
    /// --entrypoint
    /// </remarks>
    public string EntryPointCmd { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Detach) sb.Append("-d");

      sb.OptionIfExists("--entrypoint ", EntryPointCmd);
      
      // Health Check
      sb.OptionIfExists("--health-cmd ", HealthCheckCmd);
      sb.OptionIfExists("--health-interval ", HealthCheckInterval);
      sb.OptionIfExists("--health-start-period ", HealthCheckInitialPeriod);
      sb.OptionIfExists("--health-timeout ", HealthCheckCmdTimeout);
      if (HealthCheckRetries > 0) sb.AppendFormat(" --health-retries {0}", HealthCheckRetries);
      if (HealthCheckDisabled.HasValue) sb.Append(" --no-healthcheck");

      // Network settings
      sb.OptionIfExists("--dns=", Dns);
      sb.OptionIfExists("--dns-opt=", DnsOpt);
      sb.OptionIfExists("--dns-search=", DnsSearch);
      sb.OptionIfExists("--hostname ", Hostname);
// https://docs.docker.com/engine/reference/commandline/service_create/
      return sb.ToString();
    }
  }
}
/*

Usage:	docker service create [OPTIONS] IMAGE [COMMAND] [ARG...]

Create a new service

Options:
      --config config                      Specify configurations to
                                           expose to the service
      --constraint list                    Placement constraints
      --container-label list               Container labels
      --credential-spec credential-spec    Credential spec for managed
                                           service account (Windows only)
      --endpoint-mode string               Endpoint mode (vip or dnsrr)
                                           (default "vip")
  -e, --env list                           Set environment variables
      --env-file list                      Read in a file of environment
                                           variables
      --generic-resource list              User defined resources
      --group list                         Set one or more supplementary
                                           user groups for the container
      --host list                          Set one or more custom
                                           host-to-IP mappings (host:ip)
      --init                               Use an init inside each
                                           service container to forward
                                           signals and reap processes
      --isolation string                   Service container isolation mode
  -l, --label list                         Service labels
      --limit-cpu decimal                  Limit CPUs
      --limit-memory bytes                 Limit Memory
      --log-driver string                  Logging driver for service
      --log-opt list                       Logging driver options
      --mode string                        Service mode (replicated or
                                           global) (default "replicated")
      --mount mount                        Attach a filesystem mount to
                                           the service
      --name string                        Service name
      --network network                    Network attachments
      --no-resolve-image                   Do not query the registry to
                                           resolve image digest and
                                           supported platforms
      --placement-pref pref                Add a placement preference
  -p, --publish port                       Publish a port as a node port
  -q, --quiet                              Suppress progress output
      --read-only                          Mount the container's root
                                           filesystem as read only
      --replicas uint                      Number of tasks
      --reserve-cpu decimal                Reserve CPUs
      --reserve-memory bytes               Reserve Memory
      --restart-condition string           Restart when condition is met
                                           ("none"|"on-failure"|"any")
                                           (default "any")
      --restart-delay duration             Delay between restart attempts
                                           (ns|us|ms|s|m|h) (default 5s)
      --restart-max-attempts uint          Maximum number of restarts
                                           before giving up
      --restart-window duration            Window used to evaluate the
                                           restart policy (ns|us|ms|s|m|h)
      --rollback-delay duration            Delay between task rollbacks
                                           (ns|us|ms|s|m|h) (default 0s)
      --rollback-failure-action string     Action on rollback failure
                                           ("pause"|"continue") (default
                                           "pause")
      --rollback-max-failure-ratio float   Failure rate to tolerate
                                           during a rollback (default 0)
      --rollback-monitor duration          Duration after each task
                                           rollback to monitor for
                                           failure (ns|us|ms|s|m|h)
                                           (default 5s)
      --rollback-order string              Rollback order
                                           ("start-first"|"stop-first")
                                           (default "stop-first")
      --rollback-parallelism uint          Maximum number of tasks rolled
                                           back simultaneously (0 to roll
                                           back all at once) (default 1)
      --secret secret                      Specify secrets to expose to
                                           the service
      --stop-grace-period duration         Time to wait before force
                                           killing a container
                                           (ns|us|ms|s|m|h) (default 10s)
      --stop-signal string                 Signal to stop the container
  -t, --tty                                Allocate a pseudo-TTY
      --update-delay duration              Delay between updates
                                           (ns|us|ms|s|m|h) (default 0s)
      --update-failure-action string       Action on update failure
                                           ("pause"|"continue"|"rollback") (default "pause")
      --update-max-failure-ratio float     Failure rate to tolerate
                                           during an update (default 0)
      --update-monitor duration            Duration after each task
                                           update to monitor for failure
                                           (ns|us|ms|s|m|h) (default 5s)
      --update-order string                Update order
                                           ("start-first"|"stop-first")
                                           (default "stop-first")
      --update-parallelism uint            Maximum number of tasks
                                           updated simultaneously (0 to
                                           update all at once) (default 1)
  -u, --user string                        Username or UID (format:
                                           <name|uid>[:<group|gid>])
      --with-registry-auth                 Send registry authentication
                                           details to swarm agents
  -w, --workdir string                     Working directory inside the
                                           container
*/