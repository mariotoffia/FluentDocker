using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Services.Impl
{
  /// <summary>
  /// Engine scope implementation using kernel and driver.
  /// Allows switching between Windows and Linux daemon modes (Docker Desktop on Windows).
  /// </summary>
  public class EngineScope : IEngineScope
  {
    private readonly FluentDockerKernel _kernel;
    private readonly string _driverId;
    private readonly EngineScopeType _originalScope;
    private readonly EngineScopeType _targetScope;
    private EngineScopeType _currentScope;
    private bool _disposed;

    /// <summary>
    /// Creates an engine scope. Use <see cref="CreateAsync"/> for async initialization.
    /// The constructor detects the current scope synchronously which may deadlock
    /// in environments with a SynchronizationContext (e.g. ASP.NET, WPF).
    /// Prefer <see cref="CreateAsync"/> in all new code.
    /// </summary>
    internal EngineScope(
        FluentDockerKernel kernel,
        string driverId,
        EngineScopeType targetScope)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(driverId);
      _kernel = kernel;
      _driverId = driverId;
      _targetScope = targetScope;

      _originalScope = DetectCurrentScopeSync();
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
      var scope = new EngineScope(kernel, driverId, targetScope);

      if (scope._currentScope != targetScope && targetScope != EngineScopeType.Unknown)
      {
        if (targetScope == EngineScopeType.Linux)
        {
          await scope.UseLinuxAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (targetScope == EngineScopeType.Windows)
        {
          await scope.UseWindowsAsync(cancellationToken).ConfigureAwait(false);
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
      if (_disposed)
        return;

      _disposed = true;

      if (_currentScope != _originalScope && _originalScope != EngineScopeType.Unknown)
      {
        try
        {
          // Use Task.Run to avoid SynchronizationContext deadlock when called
          // from UI threads or ASP.NET contexts. Prefer DisposeAsync instead.
          if (_originalScope == EngineScopeType.Linux)
          {
            Task.Run(() => UseLinuxAsync()).GetAwaiter().GetResult();
          }
          else if (_originalScope == EngineScopeType.Windows)
          {
            Task.Run(() => UseWindowsAsync()).GetAwaiter().GetResult();
          }
        }
        catch (Exception ex)
        {
          Logger.Log($"Engine scope restore failed: {ex.Message}");
        }
      }

      GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
      if (_disposed)
        return;

      _disposed = true;

      if (_currentScope != _originalScope && _originalScope != EngineScopeType.Unknown)
      {
        try
        {
          if (_originalScope == EngineScopeType.Linux)
          {
            await UseLinuxAsync().ConfigureAwait(false);
          }
          else if (_originalScope == EngineScopeType.Windows)
          {
            await UseWindowsAsync().ConfigureAwait(false);
          }
        }
        catch (Exception ex)
        {
          Logger.Log($"Engine scope async restore failed: {ex.Message}");
        }
      }

      GC.SuppressFinalize(this);
    }

    private EngineScopeType DetectCurrentScopeSync()
    {
      try
      {
        var driver = _kernel.SysCtl<ISystemDriver>(_driverId);
        var context = new DriverContext(_driverId);

        // Use Task.Run to avoid SynchronizationContext deadlock.
        var response = Task.Run(() => driver.IsWindowsEngineAsync(context)).GetAwaiter().GetResult();

        if (response.Success)
        {
          return response.Data ? EngineScopeType.Windows : EngineScopeType.Linux;
        }
      }
      catch (Exception ex)
      {
        Logger.Log($"Engine scope detection failed: {ex.Message}");
      }

      return EngineScopeType.Unknown;
    }
  }
}

