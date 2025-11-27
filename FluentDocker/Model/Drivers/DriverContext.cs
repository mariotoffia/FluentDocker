using System.Collections.Generic;

namespace Ductus.FluentDocker.Model.Drivers
{
    /// <summary>
    /// Provides context information for driver operations, replacing DockerUri + ICertificatePaths.
    /// </summary>
    public class DriverContext
    {
        /// <summary>
        /// The driver ID for this operation.
        /// </summary>
        public string DriverId { get; set; }

        /// <summary>
        /// Host URI (e.g., "unix:///var/run/docker.sock", "tcp://localhost:2376").
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// Path to TLS certificate directory (for secure TCP connections).
        /// </summary>
        public string CertificatePath { get; set; }

        /// <summary>
        /// Whether to verify TLS certificates.
        /// </summary>
        public bool VerifyTls { get; set; } = true;

        /// <summary>
        /// Unique operation ID for tracing and correlation.
        /// </summary>
        public string OperationId { get; set; }

        /// <summary>
        /// Additional metadata for this operation.
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Creates a new driver context.
        /// </summary>
        public DriverContext()
        {
        }

        /// <summary>
        /// Creates a new driver context with the specified driver ID.
        /// </summary>
        public DriverContext(string driverId)
        {
            DriverId = driverId;
        }

        /// <summary>
        /// Creates a new driver context with the specified driver ID and host.
        /// </summary>
        public DriverContext(string driverId, string host)
        {
            DriverId = driverId;
            Host = host;
        }
    }
}
