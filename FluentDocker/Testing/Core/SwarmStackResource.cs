using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// A Docker Swarm stack test resource with async lifecycle.
  /// Deploys and tears down a stack using the <see cref="IStackDriver"/>.
  /// </summary>
  public class SwarmStackResource : ResourceBase
  {
    private readonly StackDeployConfig _config;
    private static readonly Action<ILogger, Exception> MissingSessionLabels =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, nameof(MissingSessionLabels)),
            "Swarm stack resources cannot apply FluentDocker session labels automatically; the stack name is session-scoped by default to avoid parallel-run collisions. Set DockerResourceOptions.EnableSessionLabels=false to keep the exact name, then ensure uniqueness yourself and run stack-specific cleanup for leaks.");

    /// <summary>
    /// Creates a swarm stack resource.
    /// </summary>
    /// <param name="kernel">Kernel with registered drivers.</param>
    /// <param name="config">Stack deploy configuration.</param>
    /// <param name="options">Optional resource options.</param>
    public SwarmStackResource(
        FluentDockerKernel kernel,
        StackDeployConfig config,
        DockerResourceOptions options = null)
        : base(kernel, options)
    {
      ArgumentNullException.ThrowIfNull(config);
      _config = config;
      if (string.IsNullOrWhiteSpace(config.StackName))
        throw new ArgumentException("StackName must not be null or empty.", nameof(config));

      // Swarm stacks cannot carry session labels, so two parallel jobs deploying the same
      // caller-fixed StackName would collide and tear down each other's live stack. Session-scope
      // the name by default; opt out with DockerResourceOptions.EnableSessionLabels=false.
      if (Options.EnableSessionLabels)
        _config.StackName = SessionScopedName(config.StackName, Options.SessionId);
    }

    /// <summary>
    /// The stack name used for deployment.
    /// </summary>
    public string StackName => _config.StackName;

    /// <summary>
    /// The deployment result, available after initialization.
    /// </summary>
    public StackDeployResult DeployResult { get; private set; }

    /// <summary>
    /// Lists services in the deployed stack.
    /// </summary>
    public async Task<IList<StackServiceInfo>> ListServicesAsync(
        CancellationToken cancellationToken = default)
    {
      EnsureInitialized();
      var driver = Kernel.SysCtl<IStackDriver>(DriverId);
      var result = await driver.GetServicesAsync(
          new DriverContext(DriverId), StackName, cancellationToken: cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        throw new FluentDockerException(
            $"Failed to list services for stack '{StackName}': {result.Error}");
      return result.Data;
    }

    /// <summary>
    /// Lists tasks in the deployed stack.
    /// </summary>
    public async Task<IList<StackTask>> ListTasksAsync(
        CancellationToken cancellationToken = default)
    {
      EnsureInitialized();
      var driver = Kernel.SysCtl<IStackDriver>(DriverId);
      var result = await driver.GetTasksAsync(
          new DriverContext(DriverId), StackName, cancellationToken: cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        throw new FluentDockerException(
            $"Failed to list tasks for stack '{StackName}': {result.Error}");
      return result.Data;
    }

    #region ResourceBase overrides

    /// <inheritdoc />
    protected override async Task PreflightAsync(CancellationToken cancellationToken)
    {
      await CapabilityChecks.EnsureStackSupportAsync(Kernel, DriverId, cancellationToken).ConfigureAwait(false);

      if (!Kernel.TrySysCtl<IStackDriver>(DriverId, out _))
      {
        throw new InterfaceNotSupportedException(DriverId, nameof(IStackDriver));
      }
    }

    /// <inheritdoc />
    protected override async Task ProvisionAsync(CancellationToken cancellationToken)
    {
      var generation = ProvisionGeneration;
      if (Options.EnableSessionLabels)
        MissingSessionLabels(Logger, null);

      var driver = Kernel.SysCtl<IStackDriver>(DriverId);
      var context = new DriverContext(DriverId);

      var result = await driver.DeployAsync(context, _config, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
      {
        throw new FluentDockerException(
            $"Stack deploy failed for '{_config.StackName}': {result.Error}");
      }

      var deployResult = result.Data
          ?? throw new FluentDockerException(
              $"Stack deploy for '{_config.StackName}' returned Success " +
              "but no result payload.");
      if (TryCommitProvision(generation, () =>
      {
        DeployResult = deployResult;
        ResourceName = _config.StackName;
      }))
      {
        return;
      }

      await RemoveStaleStackAsync(driver, context, generation).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task TeardownAsync(CancellationToken cancellationToken)
    {
      var driver = Kernel.SysCtl<IStackDriver>(DriverId);
      var context = new DriverContext(DriverId);
      var result = await driver.RemoveAsync(
          context, [_config.StackName], cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        throw new FluentDockerException(
            $"Failed to remove stack '{_config.StackName}': {result.Error}");
      DeployResult = null;
    }

    /// <inheritdoc />
    protected override async Task ForceRemoveAsync(CancellationToken cancellationToken)
    {
      var driver = Kernel.SysCtl<IStackDriver>(DriverId);
      var context = new DriverContext(DriverId);
      var result = await driver.RemoveAsync(
          context, [_config.StackName], cancellationToken).ConfigureAwait(false);

      if (result.Success || result.ErrorCode == ErrorCodes.Stack.NotFound)
      {
        DeployResult = null;
        return;
      }

      throw new DriverException(
          $"Failed to force-remove stack '{_config.StackName}': {result.Error}",
          result.ErrorCode,
          result.ErrorContext);
    }

    #endregion

    private void EnsureInitialized()
    {
      if (!IsInitialized)
        throw new InvalidOperationException(
            "SwarmStack resource is not initialized. Call InitializeAsync first.");
    }

    private async Task RemoveStaleStackAsync(
        IStackDriver driver,
        DriverContext context,
        int generation)
    {
      if (!ShouldCleanupRejectedProvision(generation))
        return;

      try
      {
        using var cts = new CancellationTokenSource(Options.TeardownTimeout);
        var removeTask = driver.RemoveAsync(context, [_config.StackName], cts.Token);
        var result = await removeTask.WaitAsync(cts.Token).ConfigureAwait(false);
        if (!result.Success && result.ErrorCode != ErrorCodes.Stack.NotFound)
          OrphanCleanup.MarkAbandonedLateProvision(_config.StackName, Options.SessionId);
      }
      catch
      {
        OrphanCleanup.MarkAbandonedLateProvision(_config.StackName, Options.SessionId);
      }
    }
  }
}
