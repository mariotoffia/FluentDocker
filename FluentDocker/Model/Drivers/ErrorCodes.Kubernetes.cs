#nullable enable
namespace FluentDocker.Model.Drivers
{
  public static partial class ErrorCodes
  {
    /// <summary>
    /// Kubernetes-related error codes (Podman kube play/down/generate)
    /// </summary>
    public static class Kubernetes
    {
      /// <summary>
      /// The <c>podman kube play</c> operation (applying a Kubernetes YAML file) failed.
      /// </summary>
      public const string PlayFailed = "K8S_001";

      /// <summary>
      /// The <c>podman kube down</c> operation (tearing down resources created by
      /// <c>kube play</c>) failed.
      /// </summary>
      public const string DownFailed = "K8S_002";

      /// <summary>
      /// The <c>podman kube generate</c> operation (generating Kubernetes YAML from existing
      /// resources) failed.
      /// </summary>
      public const string GenerateFailed = "K8S_003";
    }
  }
}
