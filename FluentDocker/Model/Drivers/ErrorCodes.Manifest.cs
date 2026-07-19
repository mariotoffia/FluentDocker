#nullable enable
namespace FluentDocker.Model.Drivers
{
  public static partial class ErrorCodes
  {
    /// <summary>
    /// Manifest-related error codes (Podman manifest/multi-arch)
    /// </summary>
    public static class Manifest
    {
      /// <summary>
      /// The <c>podman manifest create</c> operation failed.
      /// </summary>
      public const string CreateFailed = "MAN_001";

      /// <summary>
      /// The <c>podman manifest add</c> operation failed.
      /// </summary>
      public const string AddFailed = "MAN_002";

      /// <summary>
      /// The <c>podman manifest push</c> operation failed.
      /// </summary>
      public const string PushFailed = "MAN_003";

      /// <summary>
      /// The <c>podman manifest inspect</c> operation failed.
      /// </summary>
      public const string InspectFailed = "MAN_004";

      /// <summary>
      /// The <c>podman manifest annotate</c> operation failed.
      /// </summary>
      public const string AnnotateFailed = "MAN_005";

      /// <summary>
      /// The <c>podman manifest rm</c> operation failed.
      /// </summary>
      public const string RemoveFailed = "MAN_006";
    }
  }
}
