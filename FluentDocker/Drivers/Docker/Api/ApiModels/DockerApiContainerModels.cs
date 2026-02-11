using System.Collections.Generic;
using Newtonsoft.Json;

namespace FluentDocker.Drivers.Docker.Api.ApiModels
{
  /// <summary>
  /// Request body for POST /containers/create.
  /// Maps from ContainerCreateConfig to Docker Engine API JSON format.
  /// </summary>
  internal class CreateContainerRequest
  {
    [JsonProperty("Image")] public string Image { get; set; }
    [JsonProperty("Cmd")] public string[] Cmd { get; set; }
    [JsonProperty("Entrypoint")] public string[] Entrypoint { get; set; }
    [JsonProperty("Env")] public string[] Env { get; set; }
    [JsonProperty("WorkingDir")] public string WorkingDir { get; set; }
    [JsonProperty("User")] public string User { get; set; }
    [JsonProperty("Hostname")] public string Hostname { get; set; }
    [JsonProperty("Tty")] public bool Tty { get; set; }
    [JsonProperty("OpenStdin")] public bool OpenStdin { get; set; }
    [JsonProperty("StopSignal")] public string StopSignal { get; set; }
    [JsonProperty("StopTimeout", NullValueHandling = NullValueHandling.Ignore)]
    public int? StopTimeout { get; set; }

    [JsonProperty("Labels", NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, string> Labels { get; set; }

    [JsonProperty("ExposedPorts", NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, object> ExposedPorts { get; set; }

    [JsonProperty("HostConfig")] public HostConfigRequest HostConfig { get; set; }
    [JsonProperty("NetworkingConfig", NullValueHandling = NullValueHandling.Ignore)]
    public NetworkingConfigRequest NetworkingConfig { get; set; }

    [JsonProperty("Healthcheck", NullValueHandling = NullValueHandling.Ignore)]
    public HealthcheckRequest Healthcheck { get; set; }
  }

  internal class HostConfigRequest
  {
    [JsonProperty("PortBindings", NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, List<PortBindingRequest>> PortBindings { get; set; }

    [JsonProperty("Binds", NullValueHandling = NullValueHandling.Ignore)]
    public string[] Binds { get; set; }

    [JsonProperty("NetworkMode", NullValueHandling = NullValueHandling.Ignore)]
    public string NetworkMode { get; set; }

    [JsonProperty("RestartPolicy", NullValueHandling = NullValueHandling.Ignore)]
    public RestartPolicyRequest RestartPolicy { get; set; }

    [JsonProperty("AutoRemove")] public bool AutoRemove { get; set; }
    [JsonProperty("Privileged")] public bool Privileged { get; set; }

    [JsonProperty("Memory", NullValueHandling = NullValueHandling.Ignore)]
    public long? Memory { get; set; }

    [JsonProperty("CpuShares", NullValueHandling = NullValueHandling.Ignore)]
    public long? CpuShares { get; set; }

    [JsonProperty("Dns", NullValueHandling = NullValueHandling.Ignore)]
    public string[] Dns { get; set; }

    [JsonProperty("ExtraHosts", NullValueHandling = NullValueHandling.Ignore)]
    public string[] ExtraHosts { get; set; }

    [JsonProperty("Links", NullValueHandling = NullValueHandling.Ignore)]
    public string[] Links { get; set; }
  }

  internal class PortBindingRequest
  {
    [JsonProperty("HostIp")] public string HostIp { get; set; } = "";
    [JsonProperty("HostPort")] public string HostPort { get; set; }
  }

  internal class RestartPolicyRequest
  {
    [JsonProperty("Name")] public string Name { get; set; }
    [JsonProperty("MaximumRetryCount")] public int MaximumRetryCount { get; set; }
  }

  internal class NetworkingConfigRequest
  {
    [JsonProperty("EndpointsConfig", NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, EndpointConfigRequest> EndpointsConfig { get; set; }
  }

  internal class EndpointConfigRequest
  {
    [JsonProperty("IPAMConfig", NullValueHandling = NullValueHandling.Ignore)]
    public IpamConfigRequest IpamConfig { get; set; }
  }

  internal class IpamConfigRequest
  {
    [JsonProperty("IPv4Address", NullValueHandling = NullValueHandling.Ignore)]
    public string Ipv4Address { get; set; }

    [JsonProperty("IPv6Address", NullValueHandling = NullValueHandling.Ignore)]
    public string Ipv6Address { get; set; }
  }

  internal class HealthcheckRequest
  {
    [JsonProperty("Test")] public string[] Test { get; set; }
    [JsonProperty("Interval", NullValueHandling = NullValueHandling.Ignore)] public long? Interval { get; set; }
    [JsonProperty("Timeout", NullValueHandling = NullValueHandling.Ignore)] public long? Timeout { get; set; }
    [JsonProperty("Retries", NullValueHandling = NullValueHandling.Ignore)] public int? Retries { get; set; }
    [JsonProperty("StartPeriod", NullValueHandling = NullValueHandling.Ignore)]
    public long? StartPeriod { get; set; }
  }

  /// <summary>
  /// Response from POST /containers/create.
  /// </summary>
  internal class CreateContainerResponse
  {
    [JsonProperty("Id")] public string Id { get; set; }
    [JsonProperty("Warnings")] public List<string> Warnings { get; set; }
  }

  /// <summary>
  /// Request body for POST /containers/{id}/exec.
  /// </summary>
  internal class ExecCreateRequest
  {
    [JsonProperty("AttachStdout")] public bool AttachStdout { get; set; } = true;
    [JsonProperty("AttachStderr")] public bool AttachStderr { get; set; } = true;
    [JsonProperty("AttachStdin")] public bool AttachStdin { get; set; }
    [JsonProperty("Tty")] public bool Tty { get; set; }
    [JsonProperty("Cmd")] public string[] Cmd { get; set; }

    [JsonProperty("Env", NullValueHandling = NullValueHandling.Ignore)]
    public string[] Env { get; set; }

    [JsonProperty("WorkingDir", NullValueHandling = NullValueHandling.Ignore)]
    public string WorkingDir { get; set; }

    [JsonProperty("User", NullValueHandling = NullValueHandling.Ignore)]
    public string User { get; set; }

    [JsonProperty("Privileged")] public bool Privileged { get; set; }
    [JsonProperty("Detach")] public bool Detach { get; set; }
  }

  /// <summary>
  /// Response from POST /containers/{id}/exec.
  /// </summary>
  internal class ExecCreateResponse
  {
    [JsonProperty("Id")] public string Id { get; set; }
  }

  /// <summary>
  /// Request body for POST /exec/{id}/start.
  /// </summary>
  internal class ExecStartRequest
  {
    [JsonProperty("Detach")] public bool Detach { get; set; }
    [JsonProperty("Tty")] public bool Tty { get; set; }
  }

  /// <summary>
  /// Response from GET /exec/{id}/json.
  /// </summary>
  internal class ExecInspectResponse
  {
    [JsonProperty("ExitCode")] public int ExitCode { get; set; }
    [JsonProperty("Running")] public bool Running { get; set; }
  }

  /// <summary>
  /// Request body for POST /containers/{id}/update.
  /// </summary>
  internal class UpdateContainerRequest
  {
    [JsonProperty("Memory", NullValueHandling = NullValueHandling.Ignore)]
    public long? Memory { get; set; }

    [JsonProperty("MemorySwap", NullValueHandling = NullValueHandling.Ignore)]
    public long? MemorySwap { get; set; }

    [JsonProperty("MemoryReservation", NullValueHandling = NullValueHandling.Ignore)]
    public long? MemoryReservation { get; set; }

    [JsonProperty("CpuShares", NullValueHandling = NullValueHandling.Ignore)]
    public long? CpuShares { get; set; }

    [JsonProperty("CpuPeriod", NullValueHandling = NullValueHandling.Ignore)]
    public long? CpuPeriod { get; set; }

    [JsonProperty("CpuQuota", NullValueHandling = NullValueHandling.Ignore)]
    public long? CpuQuota { get; set; }

    [JsonProperty("CpusetCpus", NullValueHandling = NullValueHandling.Ignore)]
    public string CpusetCpus { get; set; }

    [JsonProperty("RestartPolicy", NullValueHandling = NullValueHandling.Ignore)]
    public RestartPolicyRequest RestartPolicy { get; set; }

    [JsonProperty("PidsLimit", NullValueHandling = NullValueHandling.Ignore)]
    public long? PidsLimit { get; set; }
  }

  /// <summary>
  /// Response from POST /containers/{id}/wait.
  /// </summary>
  internal class WaitContainerResponse
  {
    [JsonProperty("StatusCode")] public int StatusCode { get; set; }

    [JsonProperty("Error", NullValueHandling = NullValueHandling.Ignore)]
    public WaitContainerError Error { get; set; }
  }

  internal class WaitContainerError
  {
    [JsonProperty("Message")] public string Message { get; set; }
  }
}
