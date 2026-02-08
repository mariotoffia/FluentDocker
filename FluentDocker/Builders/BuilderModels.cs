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
    internal class WaitCondition
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
    }

    /// <summary>
    /// Lifecycle hook types.
    /// </summary>
    public enum LifecycleHookType
    {
        CopyTo,
        CopyFrom,
        Export,
        Execute
    }

    /// <summary>
    /// Represents a lifecycle hook configuration.
    /// </summary>
    public class LifecycleHook
    {
        public LifecycleHookType Type { get; set; }
        public ServiceRunningState TriggerState { get; set; }
        public string HostPath { get; set; }
        public string ContainerPath { get; set; }
        public string[] Command { get; set; }
        public bool Explode { get; set; }
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
        public string NetworkName { get; set; }
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
