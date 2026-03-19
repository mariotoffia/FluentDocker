using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// A Docker/Podman image test resource with async lifecycle.
  /// Pulls an image on initialization and optionally removes it on disposal.
  /// </summary>
  public class ImageResource : ResourceBase
  {
    private readonly string _image;
    private readonly string _tag;
    private readonly bool _removeOnDispose;

    /// <summary>
    /// Creates an image resource that pulls the specified image.
    /// </summary>
    /// <param name="kernel">Kernel with registered drivers.</param>
    /// <param name="image">Image name (e.g. "nginx", "alpine").</param>
    /// <param name="tag">Image tag (default "latest").</param>
    /// <param name="removeOnDispose">If true, remove the image on disposal.</param>
    /// <param name="options">Optional resource options.</param>
    public ImageResource(
        FluentDockerKernel kernel,
        string image,
        string tag = "latest",
        bool removeOnDispose = false,
        DockerResourceOptions options = null)
        : base(kernel, options)
    {
      ArgumentNullException.ThrowIfNull(image);
      _image = image;
      _tag = tag ?? "latest";
      _removeOnDispose = removeOnDispose;
    }

    /// <summary>
    /// The full image reference (image:tag).
    /// </summary>
    public string ImageReference => $"{_image}:{_tag}";

    /// <summary>
    /// The image ID, available after initialization.
    /// </summary>
    public string ImageId { get; private set; }

    /// <summary>
    /// Inspects the image.
    /// </summary>
    public async Task<Image> InspectAsync(CancellationToken cancellationToken = default)
    {
      EnsureInitialized();
      var driver = Kernel.SysCtl<IImageDriver>(DriverId);
      var result = await driver.InspectAsync(
          new DriverContext(DriverId), ImageReference, cancellationToken).ConfigureAwait(false);
      return result.Success ? result.Data : null;
    }

    #region ResourceBase overrides

    /// <inheritdoc />
    protected override async Task PreflightAsync(CancellationToken cancellationToken)
    {
      await CapabilityChecks.EnsureImageSupportAsync(Kernel, DriverId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task ProvisionAsync(CancellationToken cancellationToken)
    {
      ResourceName = ImageReference;

      var driver = Kernel.SysCtl<IImageDriver>(DriverId);
      var result = await driver.PullAsync(
          new DriverContext(DriverId), _image, _tag, null, cancellationToken).ConfigureAwait(false);

      if (!result.Success)
        throw new FluentDockerException(
            $"Failed to pull image '{ImageReference}': {result.Error}");

      // Resolve the image ID via inspect
      var inspect = await driver.InspectAsync(
          new DriverContext(DriverId), ImageReference, cancellationToken).ConfigureAwait(false);
      ImageId = inspect.Success ? inspect.Data?.Id : null;
    }

    /// <inheritdoc />
    protected override async Task TeardownAsync(CancellationToken cancellationToken)
    {
      if (!_removeOnDispose || string.IsNullOrEmpty(ImageId))
        return;

      var driver = Kernel.SysCtl<IImageDriver>(DriverId);
      await driver.RemoveAsync(
          new DriverContext(DriverId), ImageReference, false, false, cancellationToken).ConfigureAwait(false);
      ImageId = null;
    }

    /// <inheritdoc />
    protected override async Task ForceRemoveAsync(CancellationToken cancellationToken)
    {
      if (!_removeOnDispose)
        return;

      var id = ImageId;
      ImageId = null;
      if (string.IsNullOrEmpty(id))
        return;

      try
      {
        var driver = Kernel.SysCtl<IImageDriver>(DriverId);
        await driver.RemoveAsync(
            new DriverContext(DriverId), id, true, false, cancellationToken).ConfigureAwait(false);
      }
      catch { /* best effort */ }
    }

    #endregion

    private void EnsureInitialized()
    {
      if (!IsInitialized)
        throw new InvalidOperationException(
            "Image resource is not initialized. Call InitializeAsync first.");
    }
  }
}
