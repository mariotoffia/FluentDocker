#nullable enable
namespace FluentDocker.Model.Drivers
{
  public static partial class ErrorCodes
  {
    /// <summary>
    /// Network-related error codes
    /// </summary>
    public static class Network
    {
      /// <summary>
      /// No network matches the given identifier.
      /// </summary>
      public const string NotFound = "NET_001";

      /// <summary>
      /// A network with the same name already exists.
      /// </summary>
      public const string AlreadyExists = "NET_002";

      /// <summary>
      /// The network create operation failed.
      /// </summary>
      public const string CreateFailed = "NET_003";

      /// <summary>
      /// The network remove operation failed.
      /// </summary>
      public const string RemoveFailed = "NET_004";

      /// <summary>
      /// Connecting a container to the network failed.
      /// </summary>
      public const string ConnectFailed = "NET_005";

      /// <summary>
      /// Disconnecting a container from the network failed.
      /// </summary>
      public const string DisconnectFailed = "NET_006";

      /// <summary>
      /// The network inspect operation failed.
      /// </summary>
      public const string InspectFailed = "NET_007";

      /// <summary>
      /// The network prune operation failed.
      /// </summary>
      public const string PruneFailed = "NET_008";

      /// <summary>
      /// The network operation did not complete within its configured timeout.
      /// </summary>
      public const string Timeout = "NET_009";
    }
  }
}
