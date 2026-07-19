#nullable enable
namespace FluentDocker.Model.Drivers
{
  public static partial class ErrorCodes
  {
    /// <summary>
    /// Image-related error codes
    /// </summary>
    public static class Image
    {
      /// <summary>
      /// No image matches the given identifier or reference.
      /// </summary>
      public const string NotFound = "IMG_001";

      /// <summary>
      /// The image pull operation failed.
      /// </summary>
      public const string PullFailed = "IMG_002";

      /// <summary>
      /// The image push operation failed.
      /// </summary>
      public const string PushFailed = "IMG_003";

      /// <summary>
      /// The image build operation failed.
      /// </summary>
      public const string BuildFailed = "IMG_004";

      /// <summary>
      /// The image remove operation failed.
      /// </summary>
      public const string RemoveFailed = "IMG_005";

      /// <summary>
      /// Tagging the image failed.
      /// </summary>
      public const string TagFailed = "IMG_006";

      /// <summary>
      /// The image inspect operation failed.
      /// </summary>
      public const string InspectFailed = "IMG_007";

      /// <summary>
      /// Retrieving the image's build history failed.
      /// </summary>
      public const string HistoryFailed = "IMG_008";

      /// <summary>
      /// Saving the image to a tar archive failed.
      /// </summary>
      public const string SaveFailed = "IMG_009";

      /// <summary>
      /// Loading an image from a tar archive failed.
      /// </summary>
      public const string LoadFailed = "IMG_010";

      /// <summary>
      /// Importing a filesystem archive as an image failed.
      /// </summary>
      public const string ImportFailed = "IMG_011";

      /// <summary>
      /// The image prune operation failed.
      /// </summary>
      public const string PruneFailed = "IMG_012";
    }
  }
}
