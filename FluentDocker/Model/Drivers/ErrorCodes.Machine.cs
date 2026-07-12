#nullable enable
namespace FluentDocker.Model.Drivers
{
  public static partial class ErrorCodes
  {
    /// <summary>
    /// Machine-related error codes (Podman machine management)
    /// </summary>
    public static class Machine
    {
      /// <summary>
      /// The <c>podman machine init</c> operation (creating a new machine) failed.
      /// </summary>
      public const string InitFailed = "MACH_001";

      /// <summary>
      /// The <c>podman machine start</c> operation failed.
      /// </summary>
      public const string StartFailed = "MACH_002";

      /// <summary>
      /// The <c>podman machine stop</c> operation failed.
      /// </summary>
      public const string StopFailed = "MACH_003";

      /// <summary>
      /// The <c>podman machine rm</c> operation failed.
      /// </summary>
      public const string RemoveFailed = "MACH_004";

      /// <summary>
      /// The <c>podman machine list</c> operation failed.
      /// </summary>
      public const string ListFailed = "MACH_005";

      /// <summary>
      /// The <c>podman machine inspect</c> operation failed.
      /// </summary>
      public const string InspectFailed = "MACH_006";

      /// <summary>
      /// The <c>podman machine ssh</c> operation failed.
      /// </summary>
      public const string SshFailed = "MACH_007";

      /// <summary>
      /// The <c>podman machine set</c> operation (updating machine configuration) failed.
      /// </summary>
      public const string SetFailed = "MACH_008";

      /// <summary>
      /// The <c>podman machine info</c> operation failed.
      /// </summary>
      public const string InfoFailed = "MACH_009";

      /// <summary>
      /// The Podman machine is not running; the operation requires a running machine.
      /// Transient — start the machine and retry (see <see cref="IsTransientCode(string)"/>
      /// and <see cref="FluentDocker.Common.PodmanMachineNotRunningException"/>).
      /// </summary>
      public const string NotRunning = "MACH_010";
    }
  }
}
