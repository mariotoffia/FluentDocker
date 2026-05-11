using System;
using System.Collections.Generic;
using FluentDocker.Model.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentDocker.Model.Drivers
{
  /// <summary>
  /// Provides context information for driver operations, replacing DockerUri + ICertificatePaths.
  /// </summary>
  public class DriverContext
  {
    /// <summary>
    /// Logger factory used by the driver pack and its components.
    /// Written by <see cref="FluentDocker.Kernel.DriverRegistry"/> before
    /// <see cref="FluentDocker.Drivers.IDriverPack.InitializeAsync"/> is invoked,
    /// so packs can rely on it being the consumer-supplied factory at initialization time.
    /// </summary>
    public ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

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
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>
    /// Sudo mechanism for Docker commands.
    /// </summary>
    public SudoMechanism Sudo { get; set; } = SudoMechanism.None;

    /// <summary>
    /// Password for sudo (when Sudo is set to SudoMechanism.Password).
    /// </summary>
    public string SudoPassword { get; set; }

    /// <summary>
    /// Default shell for sudo commands (default: "bash").
    /// </summary>
    public string DefaultShell { get; set; } = "bash";

    /// <summary>
    /// Configuration for automatic Podman machine start.
    /// When non-null, the Podman driver pack will ensure a machine
    /// is running during initialization.
    /// </summary>
    public AutoStartMachineConfig AutoStartMachine { get; set; }

    /// <summary>
    /// HTTP connection timeout for Docker API driver.
    /// When null, the driver uses its default (30 seconds).
    /// </summary>
    public TimeSpan? ConnectionTimeout { get; set; }

    /// <summary>
    /// HTTP request timeout for Docker API driver long-running operations.
    /// When null, the driver uses its default (5 minutes).
    /// </summary>
    public TimeSpan? RequestTimeout { get; set; }

    /// <summary>
    /// Docker Engine API version for Docker API driver.
    /// When null, auto-negotiates via /_ping.
    /// </summary>
    public string ApiVersion { get; set; }

    /// <summary>
    /// Creates a new driver context.
    /// </summary>
    public DriverContext()
    {
    }

    /// <summary>
    /// Creates a new driver context with the specified driver ID.
    /// </summary>
    public DriverContext(string driverId) => DriverId = driverId;

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
