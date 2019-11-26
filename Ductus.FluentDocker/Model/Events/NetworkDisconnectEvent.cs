using Ductus.FluentDocker.Model.Networks;

namespace Ductus.FluentDocker.Model.Events
{
  /// <summary>
  /// Emitted when a container has disconnected from a network.
  /// </summary>
  public sealed class NetworkDisconnectEvent : FdEvent<NetworkDisconnectEvent.NetworkDisconnectActor>
  {
    public NetworkDisconnectEvent()
    {
      Action = EventAction.Disconnect;
      Type = EventType.Network;
    }

    /// <summary>
    /// Contains the network and container hash, along with which network it connected to.
    /// </summary>
    /// <remarks>
    /// The actor is the hash of the network.
    /// </remarks>
    public sealed class NetworkDisconnectActor : EventActor
    {
      /// <summary>
      /// The id (hash) of the container that connected to <see cref="Name"/> network.
      /// </summary>
      public string ContainerId { get; set; }
      /// <summary>
      /// Name of the network.
      /// </summary>
      public string Name { get; set; }
      /// <summary>
      /// The type of the network. If <see cref="NetworkType.Custom"/> it is specified in <see cref="CustomType"/>.
      /// </summary>
      public NetworkType Type { get; set; }
      /// <summary>
      /// If <see cref="NetworkType.Custom"/> the name of the network type is present here. Otherwise null.
      /// </summary>
      public string CustomType { get; set; }
    }
  }
}
