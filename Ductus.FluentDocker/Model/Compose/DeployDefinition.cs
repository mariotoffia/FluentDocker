using System.Collections.Generic;

namespace Ductus.FluentDocker.Model.Compose
{
  /// <summary>
  /// Specify configuration related to the deployment and running of services. 
  /// </summary>
  /// <remarks>
  /// Version 3 only. This only takes effect when deploying to a swarm with docker stack deploy, and is ignored by
  /// docker-compose up and docker-compose run.
  /// </remarks>
  public sealed class DeployDefinition
  {
    /// <summary>
    /// Specify a service discovery method for external clients connecting to a swarm.
    /// </summary>
    /// <remarks>
    /// Version 3.3 only.
    /// endpoint_mode: vip - Docker assigns the service a virtual IP (VIP) that acts as the “front end” for clients
    /// to reach the service on a network. Docker routes requests between the client and available worker nodes for
    /// the service, without client knowledge of how many nodes are participating in the service or their IP addresses
    /// or ports. (This is the default.)
    ///
    /// endpoint_mode: dnsrr - DNS round-robin (DNSRR) service discovery does not use a single virtual IP. Docker sets
    /// up DNS entries for the service such that a DNS query for the service name returns a list of IP addresses, and
    /// the client connects directly to one of these. DNS round-robin is useful in cases where you want to use your
    /// own load balancer, or for Hybrid Windows and Linux applications.
    ///
    /// The options for endpoint_mode also work as flags on the swarm mode CLI command docker service create. 
    /// </remarks>
    public string EndpointMode { get; set; } = "vip";
    /// <summary>
    /// Specify labels for the service.
    /// </summary>
    /// <remarks>
    /// These labels are only set on the service, and not on any containers for the service.
    /// labels:
    ///   com.example.description: "This label will appear on the web service"
    /// </remarks>
    public IDictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Either global (exactly one container per swarm node) or replicated (a specified number of containers). 
    /// </summary>
    /// <remarks>
    /// The default is replicated.
    /// </remarks>
    public string Mode { get; set; } = "replicated";
    /// <summary>
    /// Specify placement of constraints and preferences.
    /// </summary>
    public PlacementDefinition Placement { get; set; }

    /// <summary>
    /// If the service is replicated (which is the default), specify the number of containers that should be running
    /// at any given time.
    /// </summary>
    /// <remarks>
    /// Default is one.
    /// </remarks>
    public int Replicas { get; set; } = 1;
      
    public ResourcesDefinition Resources { get; set; }
    public RestartPolicyDefinition RestartPolicy { get; set; }
    public DeployConfigDefinition RollbackConfig { get; set; }
    public DeployConfigDefinition UpdateConfig { get; set; }
  }
}