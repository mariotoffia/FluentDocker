using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FluentDocker.Drivers.Docker.Api.ApiModels
{
  /// <summary>
  /// Request body for POST /containers/create.
  /// Maps from ContainerCreateConfig to Docker Engine API JSON format.
  /// </summary>
  internal sealed class CreateContainerRequest
  {
    [JsonPropertyName("Image")] public string Image { get; set; }
    [JsonPropertyName("Cmd")] public string[] Cmd { get; set; }
    [JsonPropertyName("Entrypoint")] public string[] Entrypoint { get; set; }
    [JsonPropertyName("Env")] public string[] Env { get; set; }
    [JsonPropertyName("WorkingDir")] public string WorkingDir { get; set; }
    [JsonPropertyName("User")] public string User { get; set; }
    [JsonPropertyName("Hostname")] public string Hostname { get; set; }
    [JsonPropertyName("Tty")] public bool Tty { get; set; }
    [JsonPropertyName("OpenStdin")] public bool OpenStdin { get; set; }
    [JsonPropertyName("StopSignal")] public string StopSignal { get; set; }
    [JsonPropertyName("StopTimeout")]
    public int? StopTimeout { get; set; }

    [JsonPropertyName("Labels")]
    public Dictionary<string, string> Labels { get; set; }

    [JsonPropertyName("ExposedPorts")]
    public Dictionary<string, object> ExposedPorts { get; set; }

    [JsonPropertyName("HostConfig")] public HostConfigRequest HostConfig { get; set; }
    [JsonPropertyName("NetworkingConfig")]
    public NetworkingConfigRequest NetworkingConfig { get; set; }

    [JsonPropertyName("Healthcheck")]
    public HealthcheckRequest Healthcheck { get; set; }

    [JsonPropertyName("Platform")]
    public string Platform { get; set; }
  }

  internal sealed class HostConfigRequest
  {
    [JsonPropertyName("PortBindings")]
    public Dictionary<string, List<PortBindingRequest>> PortBindings { get; set; }

    [JsonPropertyName("Binds")]
    public string[] Binds { get; set; }

    [JsonPropertyName("NetworkMode")]
    public string NetworkMode { get; set; }

    [JsonPropertyName("RestartPolicy")]
    public RestartPolicyRequest RestartPolicy { get; set; }

    [JsonPropertyName("AutoRemove")] public bool AutoRemove { get; set; }
    [JsonPropertyName("Privileged")] public bool Privileged { get; set; }

    [JsonPropertyName("Memory")]
    public long? Memory { get; set; }

    [JsonPropertyName("CpuShares")]
    public long? CpuShares { get; set; }

    [JsonPropertyName("Dns")]
    public string[] Dns { get; set; }

    [JsonPropertyName("ExtraHosts")]
    public string[] ExtraHosts { get; set; }

    [JsonPropertyName("Links")]
    public string[] Links { get; set; }

    [JsonPropertyName("CapAdd")]
    public string[] CapAdd { get; set; }

    [JsonPropertyName("CapDrop")]
    public string[] CapDrop { get; set; }

    [JsonPropertyName("SecurityOpt")]
    public string[] SecurityOpt { get; set; }

    [JsonPropertyName("ShmSize")]
    public long? ShmSize { get; set; }

    [JsonPropertyName("Tmpfs")]
    public Dictionary<string, string> Tmpfs { get; set; }

    [JsonPropertyName("Devices")]
    public List<DeviceMappingRequest> Devices { get; set; }

    [JsonPropertyName("ReadonlyRootfs")] public bool ReadonlyRootfs { get; set; }

    [JsonPropertyName("Runtime")]
    public string Runtime { get; set; }
  }

  internal sealed class DeviceMappingRequest
  {
    [JsonPropertyName("PathOnHost")] public string PathOnHost { get; set; }
    [JsonPropertyName("PathInContainer")] public string PathInContainer { get; set; }
    [JsonPropertyName("CgroupPermissions")] public string CgroupPermissions { get; set; } = "rwm";
  }

  internal sealed class PortBindingRequest
  {
    [JsonPropertyName("HostIp")] public string HostIp { get; set; } = "";
    [JsonPropertyName("HostPort")] public string HostPort { get; set; }
  }

  internal sealed class RestartPolicyRequest
  {
    [JsonPropertyName("Name")] public string Name { get; set; }
    [JsonPropertyName("MaximumRetryCount")] public int MaximumRetryCount { get; set; }
  }

  internal sealed class NetworkingConfigRequest
  {
    [JsonPropertyName("EndpointsConfig")]
    public Dictionary<string, EndpointConfigRequest> EndpointsConfig { get; set; }
  }

  internal sealed class EndpointConfigRequest
  {
    [JsonPropertyName("IPAMConfig")]
    public IpamConfigRequest IpamConfig { get; set; }

    [JsonPropertyName("Aliases")]
    public List<string> Aliases { get; set; }
  }

  internal sealed class IpamConfigRequest
  {
    [JsonPropertyName("IPv4Address")]
    public string Ipv4Address { get; set; }

    [JsonPropertyName("IPv6Address")]
    public string Ipv6Address { get; set; }
  }

  internal sealed class HealthcheckRequest
  {
    [JsonPropertyName("Test")] public string[] Test { get; set; }
    [JsonPropertyName("Interval")] public long? Interval { get; set; }
    [JsonPropertyName("Timeout")] public long? Timeout { get; set; }
    [JsonPropertyName("Retries")] public int? Retries { get; set; }
    [JsonPropertyName("StartPeriod")]
    public long? StartPeriod { get; set; }
  }

  /// <summary>
  /// Response from POST /containers/create.
  /// </summary>
  internal sealed class CreateContainerResponse
  {
    [JsonPropertyName("Id")] public string Id { get; set; }
    [JsonPropertyName("Warnings")] public List<string> Warnings { get; set; }
  }

  /// <summary>
  /// Request body for POST /containers/{id}/exec.
  /// </summary>
  internal sealed class ExecCreateRequest
  {
    [JsonPropertyName("AttachStdout")] public bool AttachStdout { get; set; } = true;
    [JsonPropertyName("AttachStderr")] public bool AttachStderr { get; set; } = true;
    [JsonPropertyName("AttachStdin")] public bool AttachStdin { get; set; }
    [JsonPropertyName("Tty")] public bool Tty { get; set; }
    [JsonPropertyName("Cmd")] public string[] Cmd { get; set; }

    [JsonPropertyName("Env")]
    public string[] Env { get; set; }

    [JsonPropertyName("WorkingDir")]
    public string WorkingDir { get; set; }

    [JsonPropertyName("User")]
    public string User { get; set; }

    [JsonPropertyName("Privileged")] public bool Privileged { get; set; }
    [JsonPropertyName("Detach")] public bool Detach { get; set; }
  }

  /// <summary>
  /// Response from POST /containers/{id}/exec.
  /// </summary>
  internal sealed class ExecCreateResponse
  {
    [JsonPropertyName("Id")] public string Id { get; set; }
  }

  /// <summary>
  /// Request body for POST /exec/{id}/start.
  /// </summary>
  internal sealed class ExecStartRequest
  {
    [JsonPropertyName("Detach")] public bool Detach { get; set; }
    [JsonPropertyName("Tty")] public bool Tty { get; set; }
  }

  /// <summary>
  /// Response from GET /exec/{id}/json.
  /// </summary>
  internal sealed class ExecInspectResponse
  {
    [JsonPropertyName("ExitCode")] public int ExitCode { get; set; }
    [JsonPropertyName("Running")] public bool Running { get; set; }
  }

  /// <summary>
  /// Request body for POST /containers/{id}/update.
  /// </summary>
  internal sealed class UpdateContainerRequest
  {
    [JsonPropertyName("Memory")]
    public long? Memory { get; set; }

    [JsonPropertyName("MemorySwap")]
    public long? MemorySwap { get; set; }

    [JsonPropertyName("MemoryReservation")]
    public long? MemoryReservation { get; set; }

    [JsonPropertyName("CpuShares")]
    public long? CpuShares { get; set; }

    [JsonPropertyName("CpuPeriod")]
    public long? CpuPeriod { get; set; }

    [JsonPropertyName("CpuQuota")]
    public long? CpuQuota { get; set; }

    [JsonPropertyName("CpusetCpus")]
    public string CpusetCpus { get; set; }

    [JsonPropertyName("RestartPolicy")]
    public RestartPolicyRequest RestartPolicy { get; set; }

    [JsonPropertyName("PidsLimit")]
    public long? PidsLimit { get; set; }
  }

  /// <summary>
  /// Response from POST /containers/{id}/wait.
  /// </summary>
  internal sealed class WaitContainerResponse
  {
    [JsonPropertyName("StatusCode")] public int StatusCode { get; set; }

    [JsonPropertyName("Error")]
    public WaitContainerError Error { get; set; }
  }

  internal sealed class WaitContainerError
  {
    [JsonPropertyName("Message")] public string Message { get; set; }
  }
}
