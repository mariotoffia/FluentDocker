using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Tests.Utilities
{
    /// <summary>
    /// Utility methods for managing test containers and networks.
    /// Provides automatic cleanup of containers/networks created during tests.
    /// </summary>
    public static class TestContainerUtils
    {
        /// <summary>
        /// Standard label key used to identify test containers.
        /// </summary>
        public const string TestLabelKey = "fluentdocker.test";

        /// <summary>
        /// Standard label key for test session ID.
        /// </summary>
        public const string TestSessionLabelKey = "fluentdocker.test.session";

        /// <summary>
        /// Creates a dictionary of labels for test containers.
        /// </summary>
        /// <param name="sessionId">Optional session ID for this test run</param>
        /// <param name="additionalLabels">Additional labels to include</param>
        /// <returns>Dictionary of labels</returns>
        public static Dictionary<string, string> CreateTestLabels(string sessionId = null, Dictionary<string, string> additionalLabels = null)
        {
            var labels = new Dictionary<string, string>
            {
                [TestLabelKey] = "true",
                [TestSessionLabelKey] = sessionId ?? Guid.NewGuid().ToString()
            };

            if (additionalLabels != null)
            {
                foreach (var label in additionalLabels)
                {
                    labels[label.Key] = label.Value;
                }
            }

            return labels;
        }

        /// <summary>
        /// Removes all test containers (identified by test label or name prefix).
        /// </summary>
        /// <param name="kernel">The FluentDocker kernel</param>
        /// <param name="driverId">The driver ID</param>
        /// <param name="sessionId">Optional session ID to filter by</param>
        /// <param name="namePrefixes">Optional name prefixes to filter by (for backward compatibility)</param>
        public static async Task CleanupTestContainersAsync(
            FluentDockerKernel kernel,
            string driverId,
            string sessionId = null,
            string[] namePrefixes = null)
        {
            try
            {
                var driver = kernel.SysCtl<IContainerDriver>(driverId);
                var context = new DriverContext(driverId);

                // List all containers
                var listResult = await driver.ListAsync(context, new ContainerListFilter { All = true });
                if (!listResult.Success || listResult.Data == null) return;

                var containersToRemove = new List<string>();

                foreach (var container in listResult.Data)
                {
                    bool shouldRemove = false;

                    // Check if container has test label
                    if (container.Config?.Labels != null && container.Config.Labels.ContainsKey(TestLabelKey))
                    {
                        // If session ID is specified, only remove containers from that session
                        if (sessionId != null)
                        {
                            if (container.Config.Labels.TryGetValue(TestSessionLabelKey, out var containerSession) &&
                                containerSession == sessionId)
                            {
                                shouldRemove = true;
                            }
                        }
                        else
                        {
                            shouldRemove = true;
                        }
                    }

                    // Fall back to name prefix check for backward compatibility
                    if (!shouldRemove && namePrefixes != null && namePrefixes.Length > 0)
                    {
                        var name = container.Name?.TrimStart('/');
                        if (name != null && namePrefixes.Any(prefix => name.StartsWith(prefix)))
                        {
                            shouldRemove = true;
                        }
                    }

                    if (shouldRemove)
                    {
                        containersToRemove.Add(container.Id);
                    }
                }

                // Remove containers
                foreach (var containerId in containersToRemove)
                {
                    try
                    {
                        await driver.RemoveAsync(context, containerId, force: true, removeVolumes: true);
                    }
                    catch
                    {
                        // Ignore individual removal errors
                    }
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        /// <summary>
        /// Removes all test networks (identified by name prefix).
        /// </summary>
        /// <param name="kernel">The FluentDocker kernel</param>
        /// <param name="driverId">The driver ID</param>
        /// <param name="namePrefixes">Name prefixes to filter by</param>
        public static async Task CleanupTestNetworksAsync(
            FluentDockerKernel kernel,
            string driverId,
            string[] namePrefixes = null)
        {
            try
            {
                var driver = kernel.SysCtl<INetworkDriver>(driverId);
                var context = new DriverContext(driverId);

                var listResult = await driver.ListAsync(context);
                if (!listResult.Success || listResult.Data == null) return;

                var defaultPrefixes = new[] { "multi-test-", "alias-test-", "oftype-test-", "all-services-", "test-" };
                var prefixes = namePrefixes ?? defaultPrefixes;

                foreach (var network in listResult.Data)
                {
                    if (network.Name != null && prefixes.Any(prefix => network.Name.StartsWith(prefix)))
                    {
                        try
                        {
                            await driver.RemoveAsync(context, network.Id);
                        }
                        catch
                        {
                            // Ignore individual removal errors
                        }
                    }
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        /// <summary>
        /// Performs comprehensive cleanup of all test resources.
        /// </summary>
        /// <param name="kernel">The FluentDocker kernel</param>
        /// <param name="driverId">The driver ID</param>
        /// <param name="sessionId">Optional session ID</param>
        public static async Task CleanupAllTestResourcesAsync(
            FluentDockerKernel kernel,
            string driverId,
            string sessionId = null)
        {
            var namePrefixes = new[]
            {
                "chain-", "link-", "multi-", "net-test-", "alias-", "volume-",
                "all-services-", "driver-filter-", "oftype-test-", "test-"
            };

            await CleanupTestContainersAsync(kernel, driverId, sessionId, namePrefixes);
            await CleanupTestNetworksAsync(kernel, driverId);
        }

        /// <summary>
        /// Lists all containers with the specified label using efficient server-side filtering.
        /// </summary>
        /// <param name="kernel">The FluentDocker kernel</param>
        /// <param name="driverId">The driver ID</param>
        /// <param name="labelKey">Label key to filter by</param>
        /// <param name="labelValue">Optional label value to filter by (null means any value)</param>
        /// <param name="includeAll">Include stopped containers (default: true)</param>
        /// <returns>List of container IDs matching the label</returns>
        public static async Task<List<string>> ListContainersByLabelAsync(
            FluentDockerKernel kernel,
            string driverId,
            string labelKey,
            string labelValue = null,
            bool includeAll = true)
        {
            try
            {
                var driver = kernel.SysCtl<IContainerDriver>(driverId);
                var context = new DriverContext(driverId);

                var filter = new ContainerListFilter
                {
                    All = includeAll,
                    Labels = new Dictionary<string, string>
                    {
                        [labelKey] = labelValue ?? string.Empty
                    }
                };

                var result = await driver.ListAsync(context, filter);
                if (!result.Success || result.Data == null)
                    return new List<string>();

                return result.Data.Select(c => c.Id).ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Removes all containers with the specified label using efficient server-side filtering.
        /// </summary>
        /// <param name="kernel">The FluentDocker kernel</param>
        /// <param name="driverId">The driver ID</param>
        /// <param name="labelKey">Label key to filter by</param>
        /// <param name="labelValue">Optional label value to filter by (null means any value)</param>
        /// <returns>Number of containers removed</returns>
        public static async Task<int> RemoveContainersByLabelAsync(
            FluentDockerKernel kernel,
            string driverId,
            string labelKey,
            string labelValue = null)
        {
            try
            {
                var driver = kernel.SysCtl<IContainerDriver>(driverId);
                var context = new DriverContext(driverId);

                var filter = new ContainerListFilter
                {
                    All = true,
                    Labels = new Dictionary<string, string>
                    {
                        [labelKey] = labelValue ?? string.Empty
                    }
                };

                var result = await driver.ListAsync(context, filter);
                if (!result.Success || result.Data == null)
                    return 0;

                int removed = 0;
                foreach (var container in result.Data)
                {
                    try
                    {
                        await driver.RemoveAsync(context, container.Id, force: true, removeVolumes: true);
                        removed++;
                    }
                    catch
                    {
                        // Ignore individual removal errors
                    }
                }

                return removed;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Removes all test containers using efficient label-based filtering.
        /// This is much faster than the legacy method as it uses server-side filtering.
        /// </summary>
        /// <param name="kernel">The FluentDocker kernel</param>
        /// <param name="driverId">The driver ID</param>
        /// <param name="sessionId">Optional session ID to filter by (null removes all test containers)</param>
        /// <returns>Number of containers removed</returns>
        public static async Task<int> RemoveTestContainersByLabelAsync(
            FluentDockerKernel kernel,
            string driverId,
            string sessionId = null)
        {
            if (sessionId != null)
            {
                // Remove containers from a specific session
                return await RemoveContainersByLabelAsync(kernel, driverId, TestSessionLabelKey, sessionId);
            }
            else
            {
                // Remove all test containers
                return await RemoveContainersByLabelAsync(kernel, driverId, TestLabelKey, "true");
            }
        }
    }
}
