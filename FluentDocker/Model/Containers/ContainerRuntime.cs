namespace Ductus.FluentDocker.Model.Containers
{
  public enum ContainerRuntime
  {
    /// <summary>
    /// Default runtime provided with docker. This is the
    /// default and is not necessary to specify.
    /// </summary>
    Default = 0,
    /// <summary>
    /// The NVIDIA container runtime to utilize the GPU.
    /// </summary>
    /// <remarks>
    /// See https://github.com/NVIDIA/nvidia-docker/wiki/Usage for usage.
    /// </remarks>
    Nvidia = 1
  }
}
