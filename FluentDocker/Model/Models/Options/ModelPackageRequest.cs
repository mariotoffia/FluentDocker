namespace FluentDocker.Model.Models.Options
{
  /// <summary>
  /// A request to package a local GGUF file into an OCI model artifact
  /// (maps to <c>docker model package</c>).
  /// </summary>
  public sealed class ModelPackageRequest
  {
    /// <summary>Path to the source GGUF file (<c>--gguf</c>).</summary>
    public string GgufPath { get; init; }

    /// <summary>The target repository:tag for the produced artifact.</summary>
    public ModelReference Target { get; init; }

    /// <summary>Push the produced artifact after building (<c>--push</c>).</summary>
    public bool Push { get; init; }

    /// <summary>
    /// Optional path to a license file to embed in the artifact (<c>--license &lt;path&gt;</c>).
    /// </summary>
    public string License { get; init; }
  }
}
