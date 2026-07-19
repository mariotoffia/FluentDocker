#nullable enable
namespace FluentDocker.Model.Drivers
{
  public static partial class ErrorCodes
  {
    /// <summary>
    /// Container-related error codes
    /// </summary>
    public static class Container
    {
      /// <summary>
      /// No container matches the given identifier.
      /// </summary>
      public const string NotFound = "CNT_001";

      /// <summary>
      /// A container with the same name already exists.
      /// </summary>
      public const string AlreadyExists = "CNT_002";

      /// <summary>
      /// The container start operation failed.
      /// </summary>
      public const string StartFailed = "CNT_003";

      /// <summary>
      /// The container stop operation failed.
      /// </summary>
      public const string StopFailed = "CNT_004";

      /// <summary>
      /// The container remove operation failed.
      /// </summary>
      public const string RemoveFailed = "CNT_005";

      /// <summary>
      /// The container create operation failed.
      /// </summary>
      public const string CreateFailed = "CNT_006";

      /// <summary>
      /// The container inspect operation failed.
      /// </summary>
      public const string InspectFailed = "CNT_007";

      /// <summary>
      /// The container is not in a state that allows the requested operation (e.g. starting
      /// an already-running container).
      /// </summary>
      public const string InvalidState = "CNT_008";

      /// <summary>
      /// Attaching to the container's stdio stream failed.
      /// </summary>
      public const string AttachFailed = "CNT_009";

      /// <summary>
      /// Executing a command inside the container failed.
      /// </summary>
      public const string ExecFailed = "CNT_010";

      /// <summary>
      /// The container restart operation failed.
      /// </summary>
      public const string RestartFailed = "CNT_011";

      /// <summary>
      /// The container pause operation failed.
      /// </summary>
      public const string PauseFailed = "CNT_012";

      /// <summary>
      /// The container unpause operation failed.
      /// </summary>
      public const string UnpauseFailed = "CNT_013";

      /// <summary>
      /// The container kill operation failed.
      /// </summary>
      public const string KillFailed = "CNT_014";

      /// <summary>
      /// Waiting for the container to reach an exit state failed.
      /// </summary>
      public const string WaitFailed = "CNT_015";

      /// <summary>
      /// Copying files to or from the container failed.
      /// </summary>
      public const string CopyFailed = "CNT_016";

      /// <summary>
      /// Exporting the container's filesystem as a tar archive failed.
      /// </summary>
      public const string ExportFailed = "CNT_017";

      /// <summary>
      /// Retrieving the container's filesystem changes (diff) failed.
      /// </summary>
      public const string DiffFailed = "CNT_018";

      /// <summary>
      /// Retrieving the container's running processes (top) failed.
      /// </summary>
      public const string TopFailed = "CNT_019";

      /// <summary>
      /// Renaming the container failed.
      /// </summary>
      public const string RenameFailed = "CNT_020";

      /// <summary>
      /// Updating the container's runtime configuration (e.g. resource limits) failed.
      /// </summary>
      public const string UpdateFailed = "CNT_021";

      /// <summary>
      /// Retrieving the container's resource usage statistics failed.
      /// </summary>
      public const string StatsFailed = "CNT_022";

      /// <summary>
      /// Retrieving the container's logs failed.
      /// </summary>
      public const string LogsFailed = "CNT_023";
    }
  }
}
