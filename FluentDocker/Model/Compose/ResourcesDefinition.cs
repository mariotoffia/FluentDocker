namespace Ductus.FluentDocker.Model.Compose
{
  /// <summary>
  /// 
  /// </summary>
  /// <remarks>
  /// Note: This replaces the older resource constraint options for non swarm mode in Compose files prior to version 3
  /// (cpu_shares, cpu_quota, cpuset, mem_limit, memswap_limit, mem_swappiness), as described in Upgrading
  /// version 2.x to 3.x.
  /// In this general example, the redis service is constrained to use no more than 50M of memory and 0.50 (50%) of
  /// available processing time (CPU), and has 20M of memory and 0.25 CPU time reserved (as always available to it).
  /// resources:
  /// limits:
  ///   cpus: '0.50'
  ///   memory: 50M
  /// reservations:
  ///   cpus: '0.25'
  ///   memory: 20M
  /// </remarks>
  public sealed class ResourcesDefinition
  {
    public ResourcesItemDefinition Limits { get; set; }
    public ResourcesItemDefinition Reservations { get; set; }
  }
}
