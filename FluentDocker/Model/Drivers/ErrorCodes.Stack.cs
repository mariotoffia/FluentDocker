#nullable enable
namespace FluentDocker.Model.Drivers
{
  public static partial class ErrorCodes
  {
    /// <summary>
    /// Stack-related error codes (Docker Swarm / Kubernetes)
    /// </summary>
#pragma warning disable CA1711 // Type name ends in 'Stack' — intentional domain name for Docker Swarm stacks
    public static class Stack
#pragma warning restore CA1711
    {
      /// <summary>
      /// No Swarm stack matches the given name.
      /// </summary>
      public const string NotFound = "STK_001";

      /// <summary>
      /// The <c>stack ls</c> operation failed.
      /// </summary>
      public const string ListFailed = "STK_002";

      /// <summary>
      /// The <c>stack ps</c> operation (listing stack tasks) failed.
      /// </summary>
      public const string TasksFailed = "STK_003";

      /// <summary>
      /// The <c>stack deploy</c> operation failed.
      /// </summary>
      public const string DeployFailed = "STK_004";

      /// <summary>
      /// The <c>stack rm</c> operation failed.
      /// </summary>
      public const string RemoveFailed = "STK_005";

      /// <summary>
      /// The <c>stack services</c> operation failed.
      /// </summary>
      public const string ServicesFailed = "STK_006";
    }
  }
}
