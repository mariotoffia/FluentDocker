using System;
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
  public class EngineScope : IEngineScope
  {
    private readonly FluentDockerKernel _kernel;
    private readonly ILogger<EngineScope> _logger;
    private readonly string _driverId;
    private readonly EngineScopeType _originalScope;
    private readonly EngineScopeType _targetScope;
    private readonly TimeSpan _disposeCleanupTimeout =
        TimeSpan.FromMilliseconds(ContainerService.DefaultDisposeCleanupTimeoutMs);
    private EngineScopeType _currentScope;
    private int _disposed;

    private EngineScope(
        FluentDockerKernel kernel,
        string driverId,
        EngineScopeType targetScope,
        EngineScopeType originalScope)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(driverId);
      _kernel = kernel;
      _logger = kernel.LoggerFactory.CreateLogger<EngineScope>();
      _driverId = driverId;
      _targetScope = targetScope;

      _originalScope = originalScope;
      _currentScope = _originalScope;
    }

    /// <summary>
    /// Creates an engine scope and immediately switches to the target scope.
    /// </summary>
    public static async Task<EngineScope> CreateAsync(
        FluentDockerKernel kernel,
        string driverId,
        EngineScopeType targetScope,
        CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(driverId);
      var logger = kernel.LoggerFactory.CreateLogger<EngineScope>();
      var originalScope = await DetectCurrentScopeAsync(
          kernel, driverId, logger, cancellationToken).ConfigureAwait(false);
      var scope = new EngineScope(kernel, driverId, targetScope, originalScope);

      if (scope._currentScope != targetScope && targetScope != EngineScopeType.Unknown)
      {
        bool switched;
        if (targetScope == EngineScopeType.Linux)
        {
          switched = await scope.UseLinuxAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (targetScope == EngineScopeType.Windows)
        {
          switched = await scope.UseWindowsAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
          switched = true;
        }

        if (!switched)
        {
          throw new DriverException(
              $"Failed to switch driver '{driverId}' engine scope from {originalScope} to {targetScope}",
              ErrorCodes.General.Unknown);
        }
      }

      return scope;
    }

    public EngineScopeType Scope => _currentScope;

    public async Task<bool> IsWindowsEngineAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<ISystemDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.IsWindowsEngineAsync(context, cancellationToken).ConfigureAwait(false);
      return response.Success && response.Data;
    }

    public async Task<bool> IsLinuxEngineAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<ISystemDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.IsLinuxEngineAsync(context, cancellationToken).ConfigureAwait(false);
      return response.Success && response.Data;
    }

    public async Task<bool> UseLinuxAsync(CancellationToken cancellationToken = default)
    {
      if (_currentScope == EngineScopeType.Linux)
        return true;

      var driver = _kernel.SysCtl<ISystemDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.SwitchToLinuxDaemonAsync(context, cancellationToken).ConfigureAwait(false);

      if (response.Success)
      {
        _currentScope = EngineScopeType.Linux;
        return true;
      }

      return false;
    }

    public async Task<bool> UseWindowsAsync(CancellationToken cancellationToken = default)
    {
      if (_currentScope == EngineScopeType.Windows)
        return true;

      var driver = _kernel.SysCtl<ISystemDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.SwitchToWindowsDaemonAsync(context, cancellationToken).ConfigureAwait(false);

      if (response.Success)
      {
        _currentScope = EngineScopeType.Windows;
        return true;
      }

      return false;
    }

    public void Dispose()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;

      if (_currentScope != _originalScope && _originalScope != EngineScopeType.Unknown)
      {
        try
        {
          // Use Task.Run to avoid SynchronizationContext deadlock when called
          // from UI threads or ASP.NET contexts. Prefer DisposeAsync instead.
          Task.Run(RestoreOriginalScopeAsync).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Engine scope restore failed");
        }
      }

      GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;

      if (_currentScope != _originalScope && _originalScope != EngineScopeType.Unknown)
      {
        try
        {
          await RestoreOriginalScopeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Engine scope async restore failed");
        }
      }

      GC.SuppressFinalize(this);
    }

    private static async Task<EngineScopeType> DetectCurrentScopeAsync(
        FluentDockerKernel kernel,
        string driverId,
        ILogger<EngineScope> logger,
        CancellationToken cancellationToken)
    {
      try
      {
        var driver = kernel.SysCtl<ISystemDriver>(driverId);
        var context = new DriverContext(driverId);
        var response = await driver.IsWindowsEngineAsync(context, cancellationToken)
            .ConfigureAwait(false);

        if (response.Success)
          return response.Data ? EngineScopeType.Windows : EngineScopeType.Linux;
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Engine scope async detection failed");
      }

      return EngineScopeType.Unknown;
    }

    private async Task RestoreOriginalScopeAsync()
    {
      using var cleanupCts = new CancellationTokenSource(_disposeCleanupTimeout);
      var restoreTask = _originalScope == EngineScopeType.Linux
          ? UseLinuxAsync(cleanupCts.Token)
          : UseWindowsAsync(cleanupCts.Token);
      await restoreTask.WaitAsync(cleanupCts.Token).ConfigureAwait(false);
    }
  }
}
