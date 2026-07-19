#nullable disable warnings
using System;
using System.Net.Http;
using FluentDocker.Common;
using FluentDocker.Services;

namespace FluentDocker.Builders
{
  #region Wait Condition Types

  /// <summary>
  /// Defines a wait condition for the container.
  /// </summary>
  internal enum WaitConditionType
  {
    Port,
    Process,
    Http,
    LogMessage,
    Healthy,
    Lambda
  }

  /// <summary>
  /// Represents a wait condition configuration.
  /// </summary>
  internal sealed class WaitCondition
  {
    public WaitConditionType Type { get; set; }
    public string Target { get; set; }
    public string Path { get; set; }
    public long TimeoutMs { get; set; }
    public HttpMethod HttpMethod { get; set; }
    public string ContentType { get; set; }
    public string Body { get; set; }
    public Func<RequestResponse, int, long> HttpContinuation { get; set; }
    public Func<IContainerService, int, int> LambdaCondition { get; set; }

    /// <summary>
    /// Delay in milliseconds between poll iterations (default 500ms).
    /// </summary>
    public int PollIntervalMs { get; set; } = 500;
  }

  /// <summary>
  /// Lifecycle hook types.
  /// </summary>
  public enum LifecycleHookType
  {
    /// <summary>Copies <see cref="LifecycleHook.HostPath"/> into the container at <see cref="LifecycleHook.ContainerPath"/>.</summary>
    CopyTo,
    /// <summary>Copies <see cref="LifecycleHook.ContainerPath"/> out of the container to <see cref="LifecycleHook.HostPath"/>.</summary>
    CopyFrom,
    /// <summary>Exports the container filesystem to <see cref="LifecycleHook.HostPath"/> (a tar file, or an exploded directory when <see cref="LifecycleHook.Explode"/> is set).</summary>
    Export,
    /// <summary>Runs <see cref="LifecycleHook.Command"/> inside the container.</summary>
    Execute
  }

  /// <summary>
  /// Represents a lifecycle hook configuration.
  /// </summary>
  public class LifecycleHook
  {
    /// <summary>The kind of action this hook performs.</summary>
    public LifecycleHookType Type { get; set; }
    /// <summary>The container state (e.g. Running, Removing) that fires this hook.</summary>
    public ServiceRunningState TriggerState { get; set; }
    /// <summary>The host-side path for <see cref="LifecycleHookType.CopyTo"/>, <see cref="LifecycleHookType.CopyFrom"/>, and <see cref="LifecycleHookType.Export"/> hooks.</summary>
    public string HostPath { get; set; }
    /// <summary>The container-side path for <see cref="LifecycleHookType.CopyTo"/> and <see cref="LifecycleHookType.CopyFrom"/> hooks.</summary>
    public string ContainerPath { get; set; }
    /// <summary>The argv command run by an <see cref="LifecycleHookType.Execute"/> hook.</summary>
    public string[] Command { get; set; }
    /// <summary>
    /// For an <see cref="LifecycleHookType.Export"/> hook, extracts the exported tar archive into
    /// <see cref="HostPath"/> as a directory instead of writing a single <c>.tar</c> file.
    /// </summary>
    public bool Explode { get; set; }
    /// <summary>
    /// Optional predicate gating an <see cref="LifecycleHookType.Export"/> hook; the export is skipped
    /// when this returns <c>false</c>.
    /// </summary>
    public Func<IContainerService, bool> Condition { get; set; }
  }

  /// <summary>
  /// Container existence behavior when name conflicts.
  /// </summary>
  public enum ContainerExistsBehavior
  {
    /// <summary>Do nothing if container exists - will fail on create.</summary>
    Default,
    /// <summary>Reuse the existing container.</summary>
    Reuse,
    /// <summary>Destroy the existing container and create new.</summary>
    Destroy
  }

  /// <summary>
  /// Network alias configuration.
  /// </summary>
  public class NetworkAlias
  {
    /// <summary>The name of the existing Docker/Podman network the alias applies to.</summary>
    public string NetworkName { get; set; }
    /// <summary>The DNS alias the container is reachable as on <see cref="NetworkName"/>.</summary>
    public string Alias { get; set; }
  }

  /// <summary>
  /// Container link configuration (legacy Docker feature).
  /// </summary>
  public class ContainerLink
  {
    /// <summary>Name of the container to link to.</summary>
    public string ContainerName { get; set; }
    /// <summary>Alias for the linked container (defaults to container name if not specified).</summary>
    public string Alias { get; set; }
  }

  #endregion
}
