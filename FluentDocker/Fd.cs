using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Services.Impl;

namespace FluentDocker
{
    /// <summary>
    /// Main entry point for FluentDocker v3.0.0.
    /// </summary>
    public static class Fd
    {
        /// <summary>
        /// Creates a new builder for constructing Docker resources.
        /// </summary>
        /// <returns>A new builder instance.</returns>
        public static Builder Build()
        {
            return new Builder();
        }

        /// <summary>
        /// Creates a new kernel builder for configuring FluentDockerKernel.
        /// </summary>
        /// <returns>A new kernel builder instance.</returns>
        public static IKernelBuilder CreateKernel()
        {
            return FluentDockerKernel.Create();
        }

        /// <summary>
        /// Creates and builds a default kernel with Docker CLI driver.
        /// </summary>
        /// <returns>A configured kernel instance.</returns>
        public static async Task<FluentDockerKernel> CreateDefaultKernelAsync(CancellationToken cancellationToken = default)
        {
            return await FluentDockerKernel.Create()
                .WithDriver("docker-cli", driver => driver.UseDockerCli().AsDefault())
                .BuildAsync(cancellationToken);
        }

        /// <summary>
        /// Creates a host service for the specified driver.
        /// </summary>
        /// <param name="kernel">The kernel instance.</param>
        /// <param name="driverId">The driver identifier.</param>
        /// <returns>A host service for interacting with Docker.</returns>
        public static IHostService GetHost(FluentDockerKernel kernel, string driverId)
        {
            return new HostService(kernel, driverId, "default", isNative: true, requireTls: false);
        }

        /// <summary>
        /// Creates an engine scope for switching between Windows and Linux daemon modes.
        /// </summary>
        /// <param name="kernel">The kernel instance.</param>
        /// <param name="driverId">The driver identifier.</param>
        /// <param name="targetScope">The target engine scope.</param>
        /// <returns>An engine scope instance.</returns>
        public static async Task<IEngineScope> EngineScopeAsync(
            FluentDockerKernel kernel,
            string driverId,
            EngineScopeType targetScope,
            CancellationToken cancellationToken = default)
        {
            return await EngineScope.CreateAsync(kernel, driverId, targetScope, cancellationToken);
        }

        /// <summary>
        /// Runs an action with a service and ensures cleanup.
        /// </summary>
        /// <typeparam name="T">Service type.</typeparam>
        /// <param name="service">The service instance.</param>
        /// <param name="run">Action to execute.</param>
        /// <param name="name">Optional name for logging.</param>
        public static async Task RunAsync<T>(T service, Func<T, Task> run, string name = null) where T : IServiceAsync
        {
            try
            {
                await service.StartAsync();
                await run.Invoke(service);
            }
            catch
            {
                if (!string.IsNullOrEmpty(name))
                    Logger.Log($"Failed to run service {name}");
                throw;
            }
            finally
            {
                try
                {
                    await service.StopAsync();
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        /// <summary>
        /// Runs a synchronous action with a service and ensures cleanup.
        /// </summary>
        /// <typeparam name="T">Service type.</typeparam>
        /// <param name="service">The service instance.</param>
        /// <param name="run">Action to execute.</param>
        /// <param name="name">Optional name for logging.</param>
        public static void Run<T>(T service, Action<T> run, string name = null) where T : IService
        {
            try
            {
                service.Start();
                run.Invoke(service);
            }
            catch
            {
                if (!string.IsNullOrEmpty(name))
                    Logger.Log($"Failed to run service {name}");
                throw;
            }
            finally
            {
                try
                {
                    service.Stop();
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }
}
