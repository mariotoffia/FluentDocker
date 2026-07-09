#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using FluentDocker.Model.Common;
using FluentDocker.Model.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentDocker.Model.Drivers
{
  /// <summary>
  /// Provides context information for driver operations, replacing DockerUri + ICertificatePaths.
  /// </summary>
  /// <remarks>
  /// Per-call contexts override registration contexts only for values explicitly set by the caller.
  /// Merge-relevant optional values therefore default to <c>null</c>; adapters apply their own
  /// runtime defaults after the merge.
  /// </remarks>
  public class DriverContext
  {
    /// <summary>
    /// Logger factory used by the driver pack and its components.
    /// Written by <see cref="FluentDocker.Kernel.DriverRegistry"/> before
    /// <see cref="FluentDocker.Drivers.IDriverPack.InitializeAsync"/> is invoked,
    /// so packs can rely on it being the consumer-supplied factory at initialization time.
    /// </summary>
    /// <remarks>
    /// This is a deliberate exception to the dependency-free Model core rule: the model carries only
    /// <c>Microsoft.Extensions.Logging.Abstractions</c> so driver packs can receive consumer logging
    /// without a FluentDocker-specific logging port. No logging behavior lives in Model, and
    /// <see cref="NullLoggerFactory.Instance"/> keeps contexts usable without dependency injection.
    /// </remarks>
    public ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

    /// <summary>
    /// The driver ID for this operation.
    /// </summary>
    public string? DriverId { get; set; }

    /// <summary>
    /// Host URI (e.g., "unix:///var/run/docker.sock", "tcp://localhost:2376").
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// Path to TLS certificate directory (for secure TCP connections).
    /// </summary>
    public string? CertificatePath { get; set; }

    /// <summary>
    /// Whether to verify TLS certificates.
    /// </summary>
    public bool? VerifyTls { get; set; }

    /// <summary>
    /// Unique operation ID for tracing and correlation.
    /// </summary>
    public string? OperationId { get; set; }

    /// <summary>
    /// Additional metadata for this operation.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Sudo mechanism for Docker commands.
    /// </summary>
    public SudoMechanism Sudo { get; set; } = SudoMechanism.None;

    /// <summary>
    /// Password for sudo (when Sudo is set to SudoMechanism.Password).
    /// </summary>
    /// <remarks>
    /// This value is sensitive. It is ignored during JSON serialization and
    /// redacted by <see cref="ToString"/>; do not log it directly.
    /// </remarks>
    [JsonIgnore]
    public string? SudoPassword { get; set; }

    /// <summary>
    /// Default shell for sudo commands. When null, the registered context or adapter default is used.
    /// </summary>
    public string? DefaultShell { get; set; }

    /// <summary>
    /// Name of the CLI binary used when registering a CLI driver pack. Per-operation
    /// values are carried for context merging but do not re-resolve an already-registered
    /// Docker CLI driver; pass this at driver registration time to use alternatives such
    /// as <c>finch</c> or <c>nerdctl</c>.
    /// </summary>
    public string? BinaryName { get; set; }

    /// <summary>
    /// Custom directories searched when registering a CLI driver pack. Per-operation
    /// values are carried for context merging but do not re-resolve an already-registered
    /// Docker CLI driver. When null or empty at registration, <c>PATH</c> is used.
    /// </summary>
    public string[]? SearchPaths { get; set; }

    /// <summary>
    /// Configuration for automatic Podman machine start.
    /// When non-null, the Podman driver pack will ensure a machine
    /// is running during initialization.
    /// </summary>
    public AutoStartMachineConfig? AutoStartMachine { get; set; }

    /// <summary>
    /// Default inference endpoint a model-capable pack binds its inference adapter to.
    /// When null the pack falls back to <see cref="ModelRunnerEndpoint.Default"/>
    /// (DOCKER_MODEL_RUNNER_URL, else host TCP). Set it to bind a non-default runner
    /// (e.g. another port/engine) once at registration instead of per-call.
    /// </summary>
    public ModelRunnerEndpoint? ModelRunnerEndpoint { get; set; }

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
    public string? ApiVersion { get; set; }

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
    public DriverContext(string driverId, string? host)
    {
      DriverId = driverId;
      Host = host;
    }

    /// <summary>
    /// Creates a registration-scoped copy with the supplied driver ID and logger factory.
    /// Mutable state is copied so later caller mutations cannot affect registered drivers.
    /// </summary>
    public DriverContext CloneWith(string driverId, ILoggerFactory? loggerFactory = null)
    {
      return new DriverContext(driverId, Host)
      {
        LoggerFactory = loggerFactory ?? LoggerFactory,
        CertificatePath = CertificatePath,
        VerifyTls = VerifyTls,
        OperationId = OperationId,
        Metadata = Metadata == null ? [] : new Dictionary<string, string>(Metadata),
        Sudo = Sudo,
        SudoPassword = SudoPassword,
        DefaultShell = DefaultShell,
        BinaryName = BinaryName,
        SearchPaths = SearchPaths == null ? null : (string[])SearchPaths.Clone(),
        AutoStartMachine = CloneAutoStartMachine(AutoStartMachine),
        ModelRunnerEndpoint = ModelRunnerEndpoint,
        ConnectionTimeout = ConnectionTimeout,
        RequestTimeout = RequestTimeout,
        ApiVersion = ApiVersion
      };
    }

    private static AutoStartMachineConfig? CloneAutoStartMachine(AutoStartMachineConfig? config)
    {
      if (config == null)
        return null;

      return new AutoStartMachineConfig
      {
        MachineName = config.MachineName,
        CreateIfNotExists = config.CreateIfNotExists,
        InitCpus = config.InitCpus,
        InitMemoryMiB = config.InitMemoryMiB,
        InitDiskSizeGiB = config.InitDiskSizeGiB,
        InitRootful = config.InitRootful
      };
    }

    /// <inheritdoc />
    public override string ToString()
    {
      var password = string.IsNullOrEmpty(SudoPassword) ? "<null>" : "***";
      return $"DriverContext(DriverId={DriverId ?? "<null>"}, Host={Host ?? "<null>"}, Sudo={Sudo}, SudoPassword={password})";
    }
  }
}
