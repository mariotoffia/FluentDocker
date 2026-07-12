#nullable enable
namespace FluentDocker.Model.Drivers
{
  public static partial class ErrorCodes
  {
    /// <summary>
    /// Compose-related error codes
    /// </summary>
    public static class Compose
    {
      /// <summary>
      /// The compose file could not be found at the given path.
      /// </summary>
      public const string FileNotFound = "CMP_001";

      /// <summary>
      /// The compose file is not valid YAML/Compose-spec format.
      /// </summary>
      public const string InvalidFormat = "CMP_002";

      /// <summary>
      /// The <c>compose up</c> operation failed.
      /// </summary>
      public const string UpFailed = "CMP_003";

      /// <summary>
      /// The <c>compose down</c> operation failed.
      /// </summary>
      public const string DownFailed = "CMP_004";

      /// <summary>
      /// The compose configuration failed validation (<c>compose config</c> reported an error).
      /// </summary>
      public const string ValidationFailed = "CMP_005";

      /// <summary>
      /// The <c>compose start</c> operation failed.
      /// </summary>
      public const string StartFailed = "CMP_006";

      /// <summary>
      /// The <c>compose stop</c> operation failed.
      /// </summary>
      public const string StopFailed = "CMP_007";

      /// <summary>
      /// The <c>compose ps</c> operation failed.
      /// </summary>
      public const string ListFailed = "CMP_008";

      /// <summary>
      /// The <c>compose logs</c> operation failed.
      /// </summary>
      public const string LogsFailed = "CMP_009";

      /// <summary>
      /// The <c>compose exec</c> operation failed.
      /// </summary>
      public const string ExecFailed = "CMP_010";

      /// <summary>
      /// The <c>compose restart</c> operation failed.
      /// </summary>
      public const string RestartFailed = "CMP_011";

      /// <summary>
      /// The <c>compose pause</c> operation failed.
      /// </summary>
      public const string PauseFailed = "CMP_012";

      /// <summary>
      /// The <c>compose unpause</c> operation failed.
      /// </summary>
      public const string UnpauseFailed = "CMP_013";

      /// <summary>
      /// The <c>compose kill</c> operation failed.
      /// </summary>
      public const string KillFailed = "CMP_014";

      /// <summary>
      /// The <c>compose rm</c> operation failed.
      /// </summary>
      public const string RemoveFailed = "CMP_015";

      /// <summary>
      /// The <c>compose top</c> operation failed.
      /// </summary>
      public const string TopFailed = "CMP_016";

      /// <summary>
      /// The <c>compose config</c> operation failed.
      /// </summary>
      public const string ConfigFailed = "CMP_017";

      /// <summary>
      /// The <c>compose images</c> operation failed.
      /// </summary>
      public const string ImagesFailed = "CMP_018";

      /// <summary>
      /// The <c>compose port</c> operation failed.
      /// </summary>
      public const string PortFailed = "CMP_019";

      /// <summary>
      /// The <c>compose build</c> operation failed.
      /// </summary>
      public const string BuildFailed = "CMP_020";

      /// <summary>
      /// The <c>compose pull</c> operation failed.
      /// </summary>
      public const string PullFailed = "CMP_021";

      /// <summary>
      /// The <c>compose push</c> operation failed.
      /// </summary>
      public const string PushFailed = "CMP_022";

      /// <summary>
      /// The <c>compose run</c> operation failed.
      /// </summary>
      public const string RunFailed = "CMP_023";

      /// <summary>
      /// The compose service scale operation failed.
      /// </summary>
      public const string ScaleFailed = "CMP_024";

      /// <summary>
      /// The <c>compose cp</c> operation failed.
      /// </summary>
      public const string CopyFailed = "CMP_025";

      /// <summary>
      /// The <c>compose create</c> operation failed.
      /// </summary>
      public const string CreateFailed = "CMP_026";
    }
  }
}
