using System;
using System.Formats.Tar;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Services.Impl
{
  public partial class ContainerService
  {
    /// <inheritdoc />
    public async Task ExportToFileAsync(string path, CancellationToken cancellationToken = default)
    {
      ArgumentException.ThrowIfNullOrEmpty(path);
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();

      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);
      var fullPath = Path.GetFullPath(path);
      var dir = Path.GetDirectoryName(fullPath);
      if (!string.IsNullOrEmpty(dir))
        Directory.CreateDirectory(dir);

      // Stream straight to the caller's file (atomic via .partial): unlike ExportAsync's byte[] this
      // has no ~2 GB / CLR-array ceiling — the export is bounded only by disk (SVC-MAJ-5).
      var partialPath = fullPath + ".partial";
      try
      {
        var response = await driver.ExportAsync(context, _containerId, partialPath, cancellationToken)
            .ConfigureAwait(false);
        if (!response.Success)
        {
          throw new DriverException(
              $"Failed to export container '{_name}': {response.Error}",
              response.ErrorCode,
              response.ErrorContext);
        }

        File.Move(partialPath, fullPath, overwrite: true);
      }
      catch
      {
        DeleteFileBestEffort(partialPath);
        throw;
      }
    }

    private async Task<string> ExportToTempFileCoreAsync(
        bool throwIfDisposed,
        CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (throwIfDisposed)
        ThrowIfDisposed();

      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);
      var tempPath = Path.GetTempFileName();
      try
      {
        var response = await driver.ExportAsync(context, _containerId, tempPath, cancellationToken)
            .ConfigureAwait(false);

        if (!response.Success)
        {
          throw new DriverException(
              $"Failed to export container '{_name}': {response.Error}",
              response.ErrorCode,
              response.ErrorContext);
        }

        return tempPath;
      }
      catch
      {
        DeleteFileBestEffort(tempPath);
        throw;
      }
    }

    private async Task ExecuteExportHookAsync(LifecycleHook hook, CancellationToken cancellationToken)
    {
      if (hook.Condition != null && !hook.Condition(this))
        return;

      var tempPath = await ExportToTempFileCoreAsync(throwIfDisposed: false, cancellationToken)
          .ConfigureAwait(false);
      try
      {
        var exportDir = Path.GetDirectoryName(hook.HostPath);
        if (!string.IsNullOrEmpty(exportDir) && !Directory.Exists(exportDir))
          Directory.CreateDirectory(exportDir);

        cancellationToken.ThrowIfCancellationRequested();
        if (hook.Explode)
        {
          Directory.CreateDirectory(hook.HostPath);
          await using var stream = File.OpenRead(tempPath);
          await TarFile.ExtractToDirectoryAsync(stream, hook.HostPath, overwriteFiles: true, cancellationToken)
              .ConfigureAwait(false);
        }
        else
        {
          var partialPath = hook.HostPath + ".partial";
          try
          {
            await using var source = File.OpenRead(tempPath);
            await using (var destination = File.Create(partialPath))
            {
              await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }
            File.Move(partialPath, hook.HostPath, overwrite: true);
          }
          catch
          {
            DeleteFileBestEffort(partialPath);
            throw;
          }
        }
      }
      finally
      {
        DeleteFileBestEffort(tempPath);
      }
    }

    private static void DeleteFileBestEffort(string path)
    {
      try
      {
        // ponytail: cleanup is best-effort and must not mask export success/failure.
        if (File.Exists(path))
          File.Delete(path);
      }
      catch (IOException)
      {
      }
      catch (UnauthorizedAccessException)
      {
      }
    }
  }
}
