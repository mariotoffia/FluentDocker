using System;
using System.Threading;
using System.Threading.Tasks;
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
        /// Creates an engine scope that automatically switches to the target scope.
        /// </summary>
        public EngineScope(
            FluentDockerKernel kernel,
            string driverId,
            EngineScopeType targetScope)
        {
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _driverId = driverId ?? throw new ArgumentNullException(nameof(driverId));
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
                    await scope.UseLinuxAsync(cancellationToken);
                }
                else if (targetScope == EngineScopeType.Windows)
                {
                    await scope.UseWindowsAsync(cancellationToken);
                }
            }
            
            return scope;
        }

        public EngineScopeType Scope => _currentScope;

        public async Task<bool> IsWindowsEngineAsync(CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<ISystemDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var response = await driver.IsWindowsEngineAsync(context, cancellationToken);
            return response.Success && response.Data;
        }

        public async Task<bool> IsLinuxEngineAsync(CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<ISystemDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var response = await driver.IsLinuxEngineAsync(context, cancellationToken);
            return response.Success && response.Data;
        }

        public async Task<bool> UseLinuxAsync(CancellationToken cancellationToken = default)
        {
            if (_currentScope == EngineScopeType.Linux)
                return true;

            var driver = _kernel.SysCtl<ISystemDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var response = await driver.SwitchToLinuxDaemonAsync(context, cancellationToken);
            
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

            var response = await driver.SwitchToWindowsDaemonAsync(context, cancellationToken);
            
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
                    if (_originalScope == EngineScopeType.Linux)
                    {
                        UseLinuxAsync().GetAwaiter().GetResult();
                    }
                    else if (_originalScope == EngineScopeType.Windows)
                    {
                        UseWindowsAsync().GetAwaiter().GetResult();
                    }
                }
                catch
                {
                }
            }
        }

#if NETSTANDARD2_0
        public async Task DisposeAsync()
#else
        public async ValueTask DisposeAsync()
#endif
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
                        await UseLinuxAsync();
                    }
                    else if (_originalScope == EngineScopeType.Windows)
                    {
                        await UseWindowsAsync();
                    }
                }
                catch
                {
                }
            }
        }

        private EngineScopeType DetectCurrentScopeSync()
        {
            try
            {
                var driver = _kernel.SysCtl<ISystemDriver>(_driverId);
                var context = new DriverContext(_driverId);

                var response = driver.IsWindowsEngineAsync(context).GetAwaiter().GetResult();
                
                if (response.Success)
                {
                    return response.Data ? EngineScopeType.Windows : EngineScopeType.Linux;
                }
            }
            catch
            {
            }

            return EngineScopeType.Unknown;
        }
    }
}

