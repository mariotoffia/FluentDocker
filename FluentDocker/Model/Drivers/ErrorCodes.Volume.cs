#nullable enable
namespace FluentDocker.Model.Drivers
{
  public static partial class ErrorCodes
  {
    /// <summary>
    /// Volume-related error codes
    /// </summary>
    public static class Volume
    {
      /// <summary>
      /// No volume matches the given name.
      /// </summary>
      public const string NotFound = "VOL_001";

      /// <summary>
      /// A volume with the same name already exists.
      /// </summary>
      public const string AlreadyExists = "VOL_002";

      /// <summary>
      /// The volume create operation failed.
      /// </summary>
      public const string CreateFailed = "VOL_003";

      /// <summary>
      /// The volume remove operation failed.
      /// </summary>
      public const string RemoveFailed = "VOL_004";

      /// <summary>
      /// The volume inspect operation failed.
      /// </summary>
      public const string InspectFailed = "VOL_005";

      /// <summary>
      /// The volume is in use by a container and cannot be removed.
      /// </summary>
      public const string InUse = "VOL_006";

      /// <summary>
      /// The volume prune operation failed.
      /// </summary>
      public const string PruneFailed = "VOL_007";

      /// <summary>
      /// Listing volumes failed.
      /// </summary>
      public const string ListFailed = "VOL_008";
    }
  }
}
