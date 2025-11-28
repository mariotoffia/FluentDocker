using System;
using System.Collections.Generic;
using System.Text;
using FluentDocker.Executors;
using FluentDocker.Executors.Parsers;
using FluentDocker.Extensions;
using FluentDocker.Model.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Stacks;

namespace FluentDocker.Commands
{
  /// <summary>
  /// Docker service commands for Docker Swarm
  /// </summary>
  /// <remarks>
  /// API 1.24+
  /// docker service create [OPTIONS] IMAGE [COMMAND] [ARG...]
  /// This class is deprecated. Use the IServiceDriver interface from the FluentDocker.Drivers namespace instead.
  /// The Driver layer provides async operations, better error handling, and support for multiple container runtimes.
  /// </remarks>
  [System.Obsolete("Use IServiceDriver from FluentDocker.Drivers namespace instead. Will be removed in v4.0.0.")]
  public static class Service
  {
    /// <summary>
    /// Creates a new service in Docker Swarm.
    /// </summary>
    /// <param name="host">The docker host.</param>
    /// <param name="image">The image to use for the service.</param>
    /// <param name="args">Optional service create arguments.</param>
    /// <returns>The service ID if successful.</returns>
    public static CommandResponse<string> ServiceCreate(this DockerUri host, string image,
      ServiceCreateCommandArgs args = default)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return
        new ProcessExecutor<SingleStringResponseParser, string>(
          "docker".ResolveBinary(),
          $"{certArgs} service create {options} {image}").Execute();
    }

    /// <summary>
    /// Lists services in Docker Swarm.
    /// </summary>
    public static CommandResponse<IList<string>> ServiceLs(this DockerUri host,
      ServiceLsCommandArgs args = default)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} service ls {options}").Execute();
    }

    /// <summary>
    /// Removes one or more services from Docker Swarm.
    /// </summary>
    public static CommandResponse<IList<string>> ServiceRm(this DockerUri host,
      ICertificatePaths certificates = null, params string[] serviceIds)
    {
      if (null == serviceIds || 0 == serviceIds.Length)
        throw new ArgumentException("Must provide service IDs when removing services.", nameof(serviceIds));

      var args = $"{host.RenderBaseArgs(certificates)}";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} service rm {string.Join(" ", serviceIds)}").Execute();
    }

    /// <summary>
    /// Gets details about a service.
    /// </summary>
    public static CommandResponse<IList<string>> ServiceInspect(this DockerUri host, string serviceId,
      bool pretty = false, ICertificatePaths certificates = null)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";
      var options = pretty ? "--pretty" : "";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} service inspect {options} {serviceId}").Execute();
    }

    /// <summary>
    /// Lists the tasks of a service.
    /// </summary>
    public static CommandResponse<IList<string>> ServicePs(this DockerUri host, string serviceId,
      ServicePsCommandArgs args = default)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} service ps {options} {serviceId}").Execute();
    }

    /// <summary>
    /// Scales one or more services.
    /// </summary>
    public static CommandResponse<IList<string>> ServiceScale(this DockerUri host,
      ICertificatePaths certificates = null, params string[] serviceReplicas)
    {
      if (null == serviceReplicas || 0 == serviceReplicas.Length)
        throw new ArgumentException("Must provide service=replicas pairs.", nameof(serviceReplicas));

      var args = $"{host.RenderBaseArgs(certificates)}";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} service scale {string.Join(" ", serviceReplicas)}").Execute();
    }

    /// <summary>
    /// Updates a service.
    /// </summary>
    public static CommandResponse<IList<string>> ServiceUpdate(this DockerUri host, string serviceId,
      ServiceUpdateCommandArgs args = default)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} service update {options} {serviceId}").Execute();
    }

    /// <summary>
    /// Fetches logs of a service or task.
    /// </summary>
    public static CommandResponse<IList<string>> ServiceLogs(this DockerUri host, string serviceOrTaskId,
      ServiceLogsCommandArgs args = default)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} service logs {options} {serviceOrTaskId}").Execute();
    }

    /// <summary>
    /// Rolls back a service to its previous version.
    /// </summary>
    public static CommandResponse<IList<string>> ServiceRollback(this DockerUri host, string serviceId,
      bool detach = false, bool quiet = false, ICertificatePaths certificates = null)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";
      var options = new StringBuilder();

      if (detach)
        options.Append(" --detach");
      if (quiet)
        options.Append(" --quiet");

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} service rollback {options} {serviceId}").Execute();
    }
  }

  /// <summary>
  /// Arguments for docker service create command.
  /// </summary>
  public struct ServiceCreateCommandArgs
  {
    /// <summary>Service name.</summary>
    public string Name { get; set; }
    /// <summary>Number of replicas.</summary>
    public int? Replicas { get; set; }
    /// <summary>Service mode: replicated or global.</summary>
    public string Mode { get; set; }
    /// <summary>Environment variables (KEY=VALUE).</summary>
    public string[] Environment { get; set; }
    /// <summary>Publish ports (hostPort:containerPort).</summary>
    public string[] Publish { get; set; }
    /// <summary>Mount volumes.</summary>
    public string[] Mounts { get; set; }
    /// <summary>Network to attach the service to.</summary>
    public string[] Networks { get; set; }
    /// <summary>Placement constraints.</summary>
    public string[] Constraints { get; set; }
    /// <summary>Labels to set on the service.</summary>
    public string[] Labels { get; set; }
    /// <summary>Container labels.</summary>
    public string[] ContainerLabels { get; set; }
    /// <summary>Command to run.</summary>
    public string[] Command { get; set; }
    /// <summary>Arguments to the command.</summary>
    public string[] Args { get; set; }
    /// <summary>Working directory inside the container.</summary>
    public string WorkDir { get; set; }
    /// <summary>Username or UID.</summary>
    public string User { get; set; }
    /// <summary>Restart condition (none, on-failure, any).</summary>
    public string RestartCondition { get; set; }
    /// <summary>Delay between restart attempts.</summary>
    public string RestartDelay { get; set; }
    /// <summary>Maximum number of restarts.</summary>
    public int? RestartMaxAttempts { get; set; }
    /// <summary>Window used to evaluate restart policy.</summary>
    public string RestartWindow { get; set; }
    /// <summary>Update parallelism.</summary>
    public int? UpdateParallelism { get; set; }
    /// <summary>Update delay.</summary>
    public string UpdateDelay { get; set; }
    /// <summary>Action on update failure (pause, continue, rollback).</summary>
    public string UpdateFailureAction { get; set; }
    /// <summary>Reserve CPUs.</summary>
    public string ReserveCpu { get; set; }
    /// <summary>Reserve memory.</summary>
    public string ReserveMemory { get; set; }
    /// <summary>Limit CPUs.</summary>
    public string LimitCpu { get; set; }
    /// <summary>Limit memory.</summary>
    public string LimitMemory { get; set; }
    /// <summary>Health check command.</summary>
    public string HealthCmd { get; set; }
    /// <summary>Health check interval.</summary>
    public string HealthInterval { get; set; }
    /// <summary>Health check timeout.</summary>
    public string HealthTimeout { get; set; }
    /// <summary>Health check retries.</summary>
    public int? HealthRetries { get; set; }
    /// <summary>Health check start period.</summary>
    public string HealthStartPeriod { get; set; }
    /// <summary>Secrets to expose to the service.</summary>
    public string[] Secrets { get; set; }
    /// <summary>Configs to expose to the service.</summary>
    public string[] Configs { get; set; }
    /// <summary>Log driver for the service.</summary>
    public string LogDriver { get; set; }
    /// <summary>Log driver options.</summary>
    public string[] LogOpts { get; set; }
    /// <summary>Endpoint mode (vip, dnsrr).</summary>
    public string EndpointMode { get; set; }
    /// <summary>Stop grace period.</summary>
    public string StopGracePeriod { get; set; }
    /// <summary>Rollback parallelism.</summary>
    public int? RollbackParallelism { get; set; }
    /// <summary>Rollback delay.</summary>
    public string RollbackDelay { get; set; }
    /// <summary>Action on rollback failure.</summary>
    public string RollbackFailureAction { get; set; }
    /// <summary>Detach immediately and don't wait for the service to converge.</summary>
    public bool Detach { get; set; }
    /// <summary>Suppress progress output.</summary>
    public bool Quiet { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--name ", Name);
      if (Replicas.HasValue)
        sb.Append($" --replicas {Replicas.Value}");
      sb.OptionIfExists("--mode ", Mode);
      sb.OptionIfExists("-e ", Environment);
      sb.OptionIfExists("-p ", Publish);
      sb.OptionIfExists("--mount ", Mounts);
      sb.OptionIfExists("--network ", Networks);
      sb.OptionIfExists("--constraint ", Constraints);
      sb.OptionIfExists("--label ", Labels);
      sb.OptionIfExists("--container-label ", ContainerLabels);
      sb.OptionIfExists("--workdir ", WorkDir);
      sb.OptionIfExists("--user ", User);
      sb.OptionIfExists("--restart-condition ", RestartCondition);
      sb.OptionIfExists("--restart-delay ", RestartDelay);
      if (RestartMaxAttempts.HasValue)
        sb.Append($" --restart-max-attempts {RestartMaxAttempts.Value}");
      sb.OptionIfExists("--restart-window ", RestartWindow);
      if (UpdateParallelism.HasValue)
        sb.Append($" --update-parallelism {UpdateParallelism.Value}");
      sb.OptionIfExists("--update-delay ", UpdateDelay);
      sb.OptionIfExists("--update-failure-action ", UpdateFailureAction);
      sb.OptionIfExists("--reserve-cpu ", ReserveCpu);
      sb.OptionIfExists("--reserve-memory ", ReserveMemory);
      sb.OptionIfExists("--limit-cpu ", LimitCpu);
      sb.OptionIfExists("--limit-memory ", LimitMemory);
      sb.OptionIfExists("--health-cmd ", HealthCmd);
      sb.OptionIfExists("--health-interval ", HealthInterval);
      sb.OptionIfExists("--health-timeout ", HealthTimeout);
      if (HealthRetries.HasValue)
        sb.Append($" --health-retries {HealthRetries.Value}");
      sb.OptionIfExists("--health-start-period ", HealthStartPeriod);
      sb.OptionIfExists("--secret ", Secrets);
      sb.OptionIfExists("--config ", Configs);
      sb.OptionIfExists("--log-driver ", LogDriver);
      sb.OptionIfExists("--log-opt ", LogOpts);
      sb.OptionIfExists("--endpoint-mode ", EndpointMode);
      sb.OptionIfExists("--stop-grace-period ", StopGracePeriod);
      if (RollbackParallelism.HasValue)
        sb.Append($" --rollback-parallelism {RollbackParallelism.Value}");
      sb.OptionIfExists("--rollback-delay ", RollbackDelay);
      sb.OptionIfExists("--rollback-failure-action ", RollbackFailureAction);
      if (Detach)
        sb.Append(" --detach");
      if (Quiet)
        sb.Append(" --quiet");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker service ls command.
  /// </summary>
  public struct ServiceLsCommandArgs
  {
    /// <summary>Filter output based on conditions.</summary>
    public string[] Filters { get; set; }
    /// <summary>Format the output.</summary>
    public string Format { get; set; }
    /// <summary>Only display IDs.</summary>
    public bool Quiet { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--filter ", Filters);
      sb.OptionIfExists("--format ", Format);
      if (Quiet)
        sb.Append(" --quiet");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker service ps command.
  /// </summary>
  public struct ServicePsCommandArgs
  {
    /// <summary>Filter output based on conditions.</summary>
    public string[] Filters { get; set; }
    /// <summary>Format the output.</summary>
    public string Format { get; set; }
    /// <summary>Do not truncate output.</summary>
    public bool NoTrunc { get; set; }
    /// <summary>Do not map IDs to names.</summary>
    public bool NoResolve { get; set; }
    /// <summary>Only display task IDs.</summary>
    public bool Quiet { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--filter ", Filters);
      sb.OptionIfExists("--format ", Format);
      if (NoTrunc)
        sb.Append(" --no-trunc");
      if (NoResolve)
        sb.Append(" --no-resolve");
      if (Quiet)
        sb.Append(" --quiet");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker service update command.
  /// </summary>
  public struct ServiceUpdateCommandArgs
  {
    /// <summary>Add or update environment variables.</summary>
    public string[] EnvAdd { get; set; }
    /// <summary>Remove environment variables.</summary>
    public string[] EnvRm { get; set; }
    /// <summary>Service image tag.</summary>
    public string Image { get; set; }
    /// <summary>Add or update mounts.</summary>
    public string[] MountAdd { get; set; }
    /// <summary>Remove mounts.</summary>
    public string[] MountRm { get; set; }
    /// <summary>Add or update published ports.</summary>
    public string[] PublishAdd { get; set; }
    /// <summary>Remove published ports.</summary>
    public string[] PublishRm { get; set; }
    /// <summary>Number of replicas.</summary>
    public int? Replicas { get; set; }
    /// <summary>Force update even if no changes.</summary>
    public bool Force { get; set; }
    /// <summary>Rollback to previous specification.</summary>
    public bool Rollback { get; set; }
    /// <summary>Detach immediately.</summary>
    public bool Detach { get; set; }
    /// <summary>Suppress progress output.</summary>
    public bool Quiet { get; set; }
    /// <summary>Add or update labels.</summary>
    public string[] LabelAdd { get; set; }
    /// <summary>Remove labels.</summary>
    public string[] LabelRm { get; set; }
    /// <summary>Add or update constraints.</summary>
    public string[] ConstraintAdd { get; set; }
    /// <summary>Remove constraints.</summary>
    public string[] ConstraintRm { get; set; }
    /// <summary>Add or update networks.</summary>
    public string[] NetworkAdd { get; set; }
    /// <summary>Remove networks.</summary>
    public string[] NetworkRm { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--env-add ", EnvAdd);
      sb.OptionIfExists("--env-rm ", EnvRm);
      sb.OptionIfExists("--image ", Image);
      sb.OptionIfExists("--mount-add ", MountAdd);
      sb.OptionIfExists("--mount-rm ", MountRm);
      sb.OptionIfExists("--publish-add ", PublishAdd);
      sb.OptionIfExists("--publish-rm ", PublishRm);
      if (Replicas.HasValue)
        sb.Append($" --replicas {Replicas.Value}");
      if (Force)
        sb.Append(" --force");
      if (Rollback)
        sb.Append(" --rollback");
      if (Detach)
        sb.Append(" --detach");
      if (Quiet)
        sb.Append(" --quiet");
      sb.OptionIfExists("--label-add ", LabelAdd);
      sb.OptionIfExists("--label-rm ", LabelRm);
      sb.OptionIfExists("--constraint-add ", ConstraintAdd);
      sb.OptionIfExists("--constraint-rm ", ConstraintRm);
      sb.OptionIfExists("--network-add ", NetworkAdd);
      sb.OptionIfExists("--network-rm ", NetworkRm);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker service logs command.
  /// </summary>
  public struct ServiceLogsCommandArgs
  {
    /// <summary>Show extra details.</summary>
    public bool Details { get; set; }
    /// <summary>Follow log output.</summary>
    public bool Follow { get; set; }
    /// <summary>Show logs since timestamp.</summary>
    public string Since { get; set; }
    /// <summary>Number of lines to show from the end.</summary>
    public int? Tail { get; set; }
    /// <summary>Show timestamps.</summary>
    public bool Timestamps { get; set; }
    /// <summary>Do not include task IDs in output.</summary>
    public bool NoTaskIds { get; set; }
    /// <summary>Do not truncate output.</summary>
    public bool NoTrunc { get; set; }
    /// <summary>Do not neatly format logs.</summary>
    public bool Raw { get; set; }
    /// <summary>Do not resolve IDs to names.</summary>
    public bool NoResolve { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Details)
        sb.Append(" --details");
      if (Follow)
        sb.Append(" --follow");
      sb.OptionIfExists("--since ", Since);
      if (Tail.HasValue)
        sb.Append($" --tail {Tail.Value}");
      if (Timestamps)
        sb.Append(" --timestamps");
      if (NoTaskIds)
        sb.Append(" --no-task-ids");
      if (NoTrunc)
        sb.Append(" --no-trunc");
      if (Raw)
        sb.Append(" --raw");
      if (NoResolve)
        sb.Append(" --no-resolve");

      return sb.ToString();
    }
  }
}
