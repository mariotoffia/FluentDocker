namespace Ductus.FluentDocker.Model.Containers
{
  public enum ContainerRuntime
  {
    /// <summary>
    /// Default runtime provided with docker. This is the
    /// default and is not neccesary to specify.
    /// </summary>
    Default = 0,
    /// <summary>
    /// The NVIDIA container runtime to utlitize the GPU.
    /// </summary>
    /// <remarks>
    /// See https://github.com/NVIDIA/nvidia-docker/wiki/Usage for usage.
    /// </remarks>
    Nvidia = 1
  }
}
