#nullable disable warnings
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Volumes;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// A Docker/Podman volume test resource with async lifecycle.
  /// Creates a named volume on initialization and removes it on disposal.
  /// </summary>
  public class VolumeResource : ResourceBase
  {
    private readonly Action<VolumeCreateConfig> _configure;

    /// <summary>
    /// Creates a volume resource.
    /// </summary>
    /// <param name="kernel">Kernel with registered drivers.</param>
    /// <param name="configure">Volume configuration callback.</param>
    /// <param name="options">Optional resource options.</param>
    public VolumeResource(
        FluentDockerKernel kernel,
        Action<VolumeCreateConfig> configure,
        DockerResourceOptions options = null)
        : base(kernel, options)
    {
      ArgumentNullException.ThrowIfNull(configure);
      _configure = configure;
    }

    /// <summary>
    /// The volume name, available after initialization.
    /// </summary>
    public string VolumeName => ResourceName;

    /// <summary>
    /// Inspects the volume.
    /// </summary>
    public async Task<Volume> InspectAsync(CancellationToken cancellationToken = default)
    {
      EnsureInitialized();
      var driver = Kernel.SysCtl<IVolumeDriver>(DriverId);
      var result = await driver.InspectAsync(
          new DriverContext(DriverId), ResourceName, cancellationToken).ConfigureAwait(false);
      return result.Success ? result.Data : null;
    }

    #region ResourceBase overrides

    /// <inheritdoc />
    protected override async Task PreflightAsync(CancellationToken cancellationToken)
    {
      await CapabilityChecks.EnsureVolumeSupportAsync(Kernel, DriverId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task ProvisionAsync(CancellationToken cancellationToken)
    {
      var generation = ProvisionGeneration;
      var config = new VolumeCreateConfig();
      _configure(config);

      var callerName = !string.IsNullOrEmpty(config.Name);
      if (string.IsNullOrEmpty(config.Name))
        config.Name = GenerateUniqueName("vol");

      if (Options.EnableSessionLabels)
      {
        foreach (var label in SessionLabel.CreateLabels(Options.SessionId))
          config.Labels[label.Key] = label.Value;
      }

      var driver = Kernel.SysCtl<IVolumeDriver>(DriverId);
      var result = await driver.CreateAsync(
          new DriverContext(DriverId), config, cancellationToken).ConfigureAwait(false);

      if (!result.Success)
        throw new FluentDockerException(
            $"Failed to create volume '{config.Name}': {result.Error}");

      var volumeName = config.Name;
      if (TryCommitProvision(generation, () => ResourceName = volumeName))
        return;

      await RemoveStaleVolumeAsync(driver, volumeName, generation, callerName).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task TeardownAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(ResourceName))
        return;

      var driver = Kernel.SysCtl<IVolumeDriver>(DriverId);
      var result = await driver.RemoveAsync(
          new DriverContext(DriverId), ResourceName, false, cancellationToken).ConfigureAwait(false);

      if (result.Success || result.ErrorCode == ErrorCodes.Volume.NotFound)
        return;

      // Genuine failure: keep ResourceName so DisposeAsync can engage ForceRemoveAsync.
      throw new DriverException(
          $"Failed to remove volume '{ResourceName}': {result.Error}",
          result.ErrorCode,
          result.ErrorContext);
    }

    /// <inheritdoc />
    protected override async Task ForceRemoveAsync(CancellationToken cancellationToken)
    {
      var name = ResourceName;
      if (string.IsNullOrEmpty(name))
        return;

      var driver = Kernel.SysCtl<IVolumeDriver>(DriverId);
      var result = await driver.RemoveAsync(
          new DriverContext(DriverId), name, true, cancellationToken).ConfigureAwait(false);

      if (result.Success || result.ErrorCode == ErrorCodes.Volume.NotFound)
        return;

      throw new DriverException(
          $"Failed to force-remove volume '{name}': {result.Error}",
          result.ErrorCode,
          result.ErrorContext);
    }

    #endregion

    private void EnsureInitialized()
    {
      if (!IsInitialized)
        throw new InvalidOperationException(
            "Volume resource is not initialized. Call InitializeAsync first.");
    }

    private async Task RemoveStaleVolumeAsync(
        IVolumeDriver driver,
        string volumeName,
        int generation,
        bool callerName)
    {
      if (callerName && !ShouldCleanupRejectedProvision(generation))
        return;

      try
      {
        using var cts = new CancellationTokenSource(Options.TeardownTimeout);
        var removeTask = driver.RemoveAsync(
            new DriverContext(DriverId), volumeName, true, cts.Token);
        var result = await removeTask.WaitAsync(cts.Token).ConfigureAwait(false);
        if (!result.Success && result.ErrorCode != ErrorCodes.Volume.NotFound)
          OrphanCleanup.MarkAbandonedLateProvision(volumeName, Options.SessionId);
      }
      catch
      {
        OrphanCleanup.MarkAbandonedLateProvision(volumeName, Options.SessionId);
      }
    }
  }
}
