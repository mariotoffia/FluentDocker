using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Services.Impl
{
  /// <inheritdoc />
  /// <remarks>
  /// Lifecycle transitions are individually atomic; a single service instance is not designed
  /// for concurrent lifecycle calls (Start/Stop/Remove/Dispose) from multiple threads.
  /// </remarks>
  public class ImageService : IImageService, IServiceCapabilities
  {
    // IServiceCapabilities
    bool IServiceCapabilities.CanStart => false;
    bool IServiceCapabilities.CanStop => false;
    bool IServiceCapabilities.CanPause => false;
    bool IServiceCapabilities.CanRemove => true;
    bool IServiceCapabilities.CanHook => true;

    private readonly FluentDockerKernel _kernel;
    private readonly ILogger<ImageService> _logger;
    private readonly string _driverId;
    private readonly string _imageId;
    private readonly string _repository;
    private readonly string _tag;
    private readonly ConcurrentDictionary<string, (ServiceRunningState State, Func<IServiceAsync, Task> Hook)> _hooks = [];
    private readonly object _stateLock = new();
    private volatile ServiceRunningState _state = ServiceRunningState.Running;

    /// <summary>
    /// Creates an image service for an existing image artifact.
    /// </summary>
    /// <param name="kernel">Kernel used to resolve image driver ports.</param>
    /// <param name="driverId">Driver id registered in the kernel.</param>
    /// <param name="imageId">Image id used for inspect, tag, save, and remove operations.</param>
    /// <param name="repository">Repository name used for push/tag display.</param>
    /// <param name="tag">Image tag; defaults to <c>latest</c> when null.</param>
    public ImageService(
        FluentDockerKernel kernel,
        string driverId,
        string imageId,
        string repository,
        string tag)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(driverId);
      ArgumentNullException.ThrowIfNull(imageId);
      _kernel = kernel;
      _logger = kernel.LoggerFactory.CreateLogger<ImageService>();
      _driverId = driverId;
      _imageId = imageId;
      _repository = repository;
      _tag = tag ?? "latest";
    }

    public string Name => FullName;
    public ServiceRunningState State => _state;
    public FluentDockerKernel Kernel => _kernel;
    public string DriverId => _driverId;
    public string Id => _imageId;
    public string Tag => _tag;
    public string FullName => string.IsNullOrEmpty(_repository)
        ? _imageId
        : IsDigestTag(_tag) ? $"{_repository}@{_tag}" : $"{_repository}:{_tag}";

    private static bool IsDigestTag(string tag)
    {
      return tag?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true;
    }

#pragma warning disable CA1710 // Delegate name 'StateChange' — intentional API design
    public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CA1710

    public async Task<Image> InspectAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<IImageDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.InspectAsync(context, _imageId, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to inspect image '{FullName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      return response.Data;
    }

    public async Task<IList<ImageLayer>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<IImageDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.HistoryAsync(context, _imageId, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to get history for image '{FullName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      return response.Data;
    }

    public async Task TagAsync(string repository, string tag, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<IImageDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.TagAsync(context, _imageId, repository, tag, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to tag image '{FullName}' as '{repository}:{tag}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }
    }

    public async Task PushAsync(IProgress<ImagePushProgress> progress = null, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<IImageDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.PushAsync(context, FullName, progress, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to push image '{FullName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }
    }

    public async Task SaveAsync(string outputPath, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<IImageDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.SaveAsync(context, [FullName], outputPath, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to save image '{FullName}' to '{outputPath}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }
    }

    /// <summary>Images are static artifacts; start is a no-op.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      throw new FluentDockerNotSupportedException("Images cannot be paused");
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      throw new FluentDockerNotSupportedException("Images cannot be stopped, use RemoveAsync instead");
    }

    public async Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      if (State == ServiceRunningState.Removed)
        return;

      var driver = _kernel.SysCtl<IImageDriver>(_driverId);
      var context = new DriverContext(_driverId);

      try
      {
        UpdateState(ServiceRunningState.Removing);
        await ExecuteHooksAsync(ServiceRunningState.Removing).ConfigureAwait(false);

        var response = await driver.RemoveAsync(context, _imageId, force, false, cancellationToken).ConfigureAwait(false);

        if (!response.Success)
        {
          if (IsImageAlreadyGone(response))
          {
            UpdateState(ServiceRunningState.Removed);
            await ExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
            return;
          }

          throw new DriverException(
              $"Failed to remove image '{FullName}': {response.Error}",
              response.ErrorCode,
              response.ErrorContext);
        }

        UpdateState(ServiceRunningState.Removed);
        await ExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
      }
      catch
      {
        UpdateState(ServiceRunningState.Unknown);
        throw;
      }
    }

    // Docker's image-remove driver maps "No such image" to the typed NotFound code and Podman sets
    // it directly, so the typed code plus the specific phrase cover both engines. No bare "not found"
    // fallback — it would mask unrelated failures (e.g. a missing registry/manifest during rmi).
    private static bool IsImageAlreadyGone(CommandResponse<ImageRemoveResult> response) =>
        response.ErrorCode == ErrorCodes.Image.NotFound ||
        response.Error?.Contains("no such image", StringComparison.OrdinalIgnoreCase) == true;

    public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null)
    {
      ThrowIfDisposed();
      var name = uniqueName ?? Guid.NewGuid().ToString();
      _hooks[name] = (state, hook);
      return this;
    }

    public IServiceAsync RemoveHook(string uniqueName)
    {
      ThrowIfDisposed();
      _hooks.TryRemove(uniqueName, out _);
      return this;
    }

    private int _disposed;
    private int _disposeCompleted;

    public void Dispose()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;
      try
      {
        // Dispatched to the thread pool to avoid sync-over-async deadlocks.
        Task.Run(() => ImageService.DisposeCoreAsync().AsTask()).GetAwaiter().GetResult();
      }
      finally
      {
        Volatile.Write(ref _disposeCompleted, 1);
        GC.SuppressFinalize(this);
      }
    }

    public async ValueTask DisposeAsync()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;
      try
      {
        await ImageService.DisposeCoreAsync().ConfigureAwait(false);
      }
      finally
      {
        Volatile.Write(ref _disposeCompleted, 1);
        GC.SuppressFinalize(this);
      }
    }

    private static async ValueTask DisposeCoreAsync()
    {
      await Task.CompletedTask.ConfigureAwait(false);
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeCompleted) != 0, this);

    private void UpdateState(ServiceRunningState newState)
    {
      ServiceDelegates.StateChange stateChange;
      StateChangeEventArgs args;
      lock (_stateLock)
      {
        if (Volatile.Read(ref _disposeCompleted) != 0 || _state == newState)
          return;

        _state = newState;
        stateChange = StateChange;
        if (stateChange == null)
          return;

        args = new StateChangeEventArgs(this, newState);
      }

      StateChangeNotifier.Invoke(stateChange, args, _logger, "ImageService");
    }

    private async Task ExecuteHooksAsync(ServiceRunningState state)
    {
      if (Volatile.Read(ref _disposeCompleted) != 0)
        return;

      foreach (var entry in _hooks.Values)
      {
        if (entry.State != state)
          continue;

        try
        {
          await entry.Hook(this).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "ImageService hook execution failed");
        }
      }
    }
  }
}
