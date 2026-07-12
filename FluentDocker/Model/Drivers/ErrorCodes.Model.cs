#nullable enable
namespace FluentDocker.Model.Drivers
{
  public static partial class ErrorCodes
  {
    /// <summary>
    /// Model management / runtime error codes (Docker Model Runner and equivalents).
    /// </summary>
    public static class Model
    {
      /// <summary>
      /// No model matches the given reference.
      /// </summary>
      public const string NotFound = "MDL_001";

      /// <summary>
      /// The <c>model pull</c> operation failed.
      /// </summary>
      public const string PullFailed = "MDL_002";

      /// <summary>
      /// The <c>model run</c> operation failed.
      /// </summary>
      public const string RunFailed = "MDL_003";

      /// <summary>
      /// The <c>model ls</c> operation failed.
      /// </summary>
      public const string ListFailed = "MDL_004";

      /// <summary>
      /// The <c>model configure</c> operation failed.
      /// </summary>
      public const string ConfigureFailed = "MDL_005";

      /// <summary>
      /// The <c>model rm</c> operation failed.
      /// </summary>
      public const string RemoveFailed = "MDL_006";

      /// <summary>
      /// The <c>model inspect</c> operation failed.
      /// </summary>
      public const string InspectFailed = "MDL_007";

      /// <summary>
      /// The <c>model tag</c> operation failed.
      /// </summary>
      public const string TagFailed = "MDL_008";

      /// <summary>
      /// The <c>model push</c> operation failed.
      /// </summary>
      public const string PushFailed = "MDL_009";

      /// <summary>
      /// The <c>model package</c> operation failed.
      /// </summary>
      public const string PackageFailed = "MDL_010";

      /// <summary>
      /// The <c>model prune</c> operation failed.
      /// </summary>
      public const string PruneFailed = "MDL_011";

      /// <summary>
      /// The <c>model run -d</c> operation (loading a model into the runner for later
      /// inference) failed.
      /// </summary>
      public const string LoadFailed = "MDL_012";

      /// <summary>
      /// The <c>model unload</c> operation failed.
      /// </summary>
      public const string UnloadFailed = "MDL_013";

      /// <summary>
      /// The Docker Model Runner engine is not running.
      /// </summary>
      public const string RunnerNotRunning = "MDL_014";

      /// <summary>
      /// The Docker Model Runner is not installed on the host.
      /// </summary>
      public const string RunnerNotInstalled = "MDL_015";

      /// <summary>
      /// The supplied model name/reference is malformed or fails validation.
      /// </summary>
      public const string InvalidReference = "MDL_016";

      /// <summary>
      /// The <c>model df</c> (disk usage) operation failed.
      /// </summary>
      public const string DiskUsageFailed = "MDL_017";

      /// <summary>
      /// The <c>model status</c> operation failed.
      /// </summary>
      public const string StatusFailed = "MDL_018";

      /// <summary>
      /// The <c>model version</c> operation failed.
      /// </summary>
      public const string VersionFailed = "MDL_019";

      /// <summary>
      /// The <c>model logs</c> operation failed.
      /// </summary>
      public const string LogsFailed = "MDL_020";

      /// <summary>
      /// The <c>model install-runner</c> operation failed.
      /// </summary>
      public const string InstallFailed = "MDL_021";

      /// <summary>
      /// The requested model operation is not supported by this driver.
      /// </summary>
      public const string NotSupported = "MDL_022";

      /// <summary>
      /// The <c>model uninstall-runner</c> operation failed.
      /// </summary>
      public const string UninstallFailed = "MDL_023";

      /// <summary>
      /// The Docker CLI does not recognize the <c>model</c> subcommand (its stderr contains
      /// "is not a docker command"); the Docker Model plugin or Docker Desktop's Model Runner
      /// is not installed.
      /// </summary>
      public const string PluginMissing = "MDL_024";
    }
  }
}
