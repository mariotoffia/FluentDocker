using System;
using System.Threading;
using System.Threading.Tasks;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Kernel;

namespace Ductus.FluentDocker.Services.V3
{
    /// <summary>
    /// v3.0.0 async service interface.
    /// </summary>
    public interface IServiceAsync : IAsyncDisposable, IDisposable
    {
        /// <summary>
        /// Service name.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Current state of the service.
        /// </summary>
        ServiceRunningState State { get; }

        /// <summary>
        /// The kernel instance managing this service.
        /// </summary>
        FluentDockerKernel Kernel { get; }

        /// <summary>
        /// The driver ID used by this service.
        /// </summary>
        string DriverId { get; }

        /// <summary>
        /// Starts the service asynchronously.
        /// </summary>
        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Pauses the service asynchronously (if supported).
        /// </summary>
        Task PauseAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops the service asynchronously.
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes the service asynchronously.
        /// </summary>
        Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a state change hook.
        /// </summary>
        IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null);

        /// <summary>
        /// Removes a hook by name.
        /// </summary>
        IServiceAsync RemoveHook(string uniqueName);

        /// <summary>
        /// State change event.
        /// </summary>
        event ServiceDelegates.StateChange StateChange;

        // Backward compatibility - sync versions (optional implementation)
        void Start() => StartAsync().GetAwaiter().GetResult();
        void Pause() => PauseAsync().GetAwaiter().GetResult();
        void Stop() => StopAsync().GetAwaiter().GetResult();
        void Remove(bool force = false) => RemoveAsync(force).GetAwaiter().GetResult();
    }
}
