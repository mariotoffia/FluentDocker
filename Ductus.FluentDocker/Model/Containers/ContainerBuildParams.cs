using System.Text;
using Ductus.FluentDocker.Extensions;

namespace Ductus.FluentDocker.Model.Containers
{
  public sealed class ContainerBuildParams
  {
    /// <summary>
    ///   Set build-time variables
    /// </summary>
    /// <remarks>
    ///   --build-arg=[]
    /// </remarks>
    public string[] BuildArguments { get; set; }

    /// <summary>
    ///   CPU shares (relative weight)
    /// </summary>
    /// <remarks>
    ///   --cpu-shares
    /// </remarks>
    public float? CpuShares { get; set; }

    /// <summary>
    ///   Optional parent cgroup for the container
    /// </summary>
    /// <remarks>
    ///   --cgroup-parent
    /// </remarks>
    public int? ParentCGroup { get; set; }

    /// <summary>
    ///   Limit the CPU CFS (Completely Fair Scheduler) period
    /// </summary>
    /// <remarks>
    ///   --cpu-period
    /// </remarks>
    public float? CpuPeriod { get; set; }

    /// <summary>
    ///   Limit the CPU CFS (Completely Fair Scheduler) quota
    /// </summary>
    /// <remarks>
    ///   --cpu-quota
    /// </remarks>
    public float? CpuQuota { get; set; }

    /// <summary>
    ///   CPUs in which to allow execution (0-3, 0,1)
    /// </summary>
    /// <remarks>
    ///   --cpuset-cpus
    /// </remarks>
    public string AllowCpuExecution { get; set; }

    /// <summary>
    ///   MEMs in which to allow execution (0-3, 0,1)
    /// </summary>
    /// <remarks>
    ///   --cpuset-mems
    /// </remarks>
    public string AllowMemExecution { get; set; }

    /// <summary>
    ///   Skip image verification
    /// </summary>
    /// <remarks>
    ///   --disable-content-trust=
    /// </remarks>
    public bool SkipImageVerification { get; set; }

    /// <summary>
    ///   Name and optionally a tag in the 'name:tag' format
    /// </summary>
    /// <remarks>
    ///   -t, --tag=[]
    /// </remarks>
    public string[] Tags { get; set; }

    /// <summary>
    ///   Name of the Dockerfile (Default is 'PATH/Dockerfile')
    /// </summary>
    /// <remarks>
    ///   -f, --file
    /// </remarks>
    public string File { get; set; }

    /// <summary>
    ///   Always remove intermediate containers
    /// </summary>
    /// <remarks>
    ///   --force-rm
    /// </remarks>
    public bool ForceRemoveIntermediateContainers { get; set; }

    /// <summary>
    ///   Set metadata for an image
    /// </summary>
    /// <remarks>
    ///   --label=[]
    /// </remarks>
    public string[] Labels { get; set; }

    /// <summary>
    ///   Container isolation technology
    /// </summary>
    /// <remarks>
    ///   --isolation
    /// </remarks>
    public ContainerIsolationTechnology Isolation { get; set; }

    /// <summary>
    ///   Memory limit
    /// </summary>
    /// <remarks>
    ///   -m, --memory
    /// </remarks>
    public long? Memory { get; set; }

    /// <summary>
    ///   Swap limit equal to memory plus swap: '-1' to enable unlimited swap
    /// </summary>
    /// <remarks>
    ///   --memory-swap
    /// </remarks>
    public long? Swap { get; set; }

    /// <summary>
    ///   Always attempt to pull a newer version of the image
    /// </summary>
    /// <remarks>
    ///   --pull
    /// </remarks>
    public bool AlwaysPull { get; set; }

    /// <summary>
    ///   Suppress the build output and print image ID on success
    /// </summary>
    /// <remarks>
    ///   -q,--quiet
    /// </remarks>
    public bool Quiet { get; set; }

    /// <summary>
    ///   Remove intermediate containers after a successful build
    /// </summary>
    /// <remarks>
    ///   --rm=true
    /// </remarks>
    public bool RemoveIntermediateContainersOnSuccessfulBuild { get; set; }

    /// <summary>
    ///   Size of /dev/shm, default value is 64MB
    /// </summary>
    /// <remarks>
    ///   --shm-size
    /// </remarks>
    public long? ShmSize { get; set; }

    /// <summary>
    ///   Ulimit options
    /// </summary>
    /// <remarks>
    ///   --ulimit=[]
    /// </remarks>
    public string[] UlimitOptions { get; set; }

    /// <summary>
    ///   Do not use cache when building the image
    /// </summary>
    /// <remarks>
    ///   --no-cache
    /// </remarks>
    public bool NoCache { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.RenderIfExists("--build-arg=", BuildArguments);
      sb.RenderIfExists("--cpu-shares ", CpuShares?.ToString());
      sb.RenderIfExists("--cgroup-parent ", ParentCGroup?.ToString());
      sb.RenderIfExists("--cpu-period ", CpuPeriod?.ToString());
      sb.RenderIfExists("--cpu-quota ", CpuQuota?.ToString());
      sb.RenderIfExists("--cpuset-cpus", AllowCpuExecution);
      sb.RenderIfExists("--cpuset-mems ", AllowMemExecution);

      if (SkipImageVerification)
      {
        sb.Append(" --disable-content-trust=true");
      }

      sb.RenderIfExists("-f ", File);

      if (ForceRemoveIntermediateContainers)
      {
        sb.Append(" --force-rm");
      }

      if (null != Isolation.ToDockerString())
      {
        sb.Append($"--isolation {Isolation.ToDockerString()}");
      }

      sb.RenderIfExists("--label=", Labels);
      sb.RenderIfExists("-m ", Memory?.ToString());
      sb.RenderIfExists("--memory-swap ", Swap?.ToString());

      if (NoCache)
      {
        sb.Append(" --no-cache");
      }

      if (AlwaysPull)
      {
        sb.Append(" --pull");
      }

      if (Quiet)
      {
        sb.Append(" -q");
      }

      if (RemoveIntermediateContainersOnSuccessfulBuild)
      {
        sb.Append(" --rm=true");
      }

      sb.RenderIfExists("--shm-size ", ShmSize?.ToString());
      sb.RenderIfExists("-t ", Tags);
      sb.RenderIfExists("--ulimit=", UlimitOptions);
      if (NoCache)
      {
        sb.Append(" --no-cache");
      }

      return sb.ToString();
    }
  }
}