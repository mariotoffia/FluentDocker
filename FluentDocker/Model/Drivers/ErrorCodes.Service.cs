#nullable enable
namespace FluentDocker.Model.Drivers
{
  public static partial class ErrorCodes
  {
    /// <summary>
    /// Service-related error codes (Docker Swarm / Kubernetes)
    /// </summary>
    public static class Service
    {
      /// <summary>
      /// No Swarm service matches the given identifier.
      /// </summary>
      public const string NotFound = "SVC_001";

      /// <summary>
      /// The <c>service create</c> operation failed.
      /// </summary>
      public const string CreateFailed = "SVC_002";

      /// <summary>
      /// The <c>service rm</c> operation failed.
      /// </summary>
      public const string RemoveFailed = "SVC_003";

      /// <summary>
      /// The <c>service update</c> operation failed.
      /// </summary>
      public const string UpdateFailed = "SVC_004";

      /// <summary>
      /// The <c>service rollback</c> operation failed.
      /// </summary>
      public const string RollbackFailed = "SVC_005";

      /// <summary>
      /// The <c>service ls</c> operation failed.
      /// </summary>
      public const string ListFailed = "SVC_006";

      /// <summary>
      /// The <c>service inspect</c> operation failed.
      /// </summary>
      public const string InspectFailed = "SVC_007";

      /// <summary>
      /// The <c>service ps</c> operation (listing service tasks) failed.
      /// </summary>
      public const string TasksFailed = "SVC_008";

      /// <summary>
      /// The <c>service logs</c> operation failed.
      /// </summary>
      public const string LogsFailed = "SVC_009";

      /// <summary>
      /// The <c>service scale</c> operation failed.
      /// </summary>
      public const string ScaleFailed = "SVC_010";
    }
  }
}
