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
          new DriverContext(DriverId), ResourceName, cancellationToken);
      return result.Success ? result.Data : null;
    }

    #region ResourceBase overrides

    /// <inheritdoc />
    protected override async Task PreflightAsync(CancellationToken cancellationToken)
    {
      await CapabilityChecks.EnsureVolumeSupportAsync(Kernel, DriverId, cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ProvisionAsync(CancellationToken cancellationToken)
    {
      var config = new VolumeCreateConfig();
      _configure(config);

      if (string.IsNullOrEmpty(config.Name))
        config.Name = GenerateUniqueName("vol");

      if (Options.EnableSessionLabels)
      {
        foreach (var label in SessionLabel.CreateLabels(Options.SessionId))
          config.Labels[label.Key] = label.Value;
      }

      ResourceName = config.Name;

      var driver = Kernel.SysCtl<IVolumeDriver>(DriverId);
      var result = await driver.CreateAsync(
          new DriverContext(DriverId), config, cancellationToken);

      if (!result.Success)
        throw new FluentDockerException(
            $"Failed to create volume '{config.Name}': {result.Error}");
    }

    /// <inheritdoc />
    protected override async Task TeardownAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(ResourceName))
        return;

      var driver = Kernel.SysCtl<IVolumeDriver>(DriverId);
      await driver.RemoveAsync(
          new DriverContext(DriverId), ResourceName, false, cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ForceRemoveAsync(CancellationToken cancellationToken)
    {
      var name = ResourceName;
      if (string.IsNullOrEmpty(name))
        return;

      try
      {
        var driver = Kernel.SysCtl<IVolumeDriver>(DriverId);
        await driver.RemoveAsync(
            new DriverContext(DriverId), name, true, cancellationToken);
      }
      catch { /* best effort */ }
    }

    #endregion

    private void EnsureInitialized()
    {
      if (!IsInitialized)
        throw new InvalidOperationException(
            "Volume resource is not initialized. Call InitializeAsync first.");
    }
  }
}
