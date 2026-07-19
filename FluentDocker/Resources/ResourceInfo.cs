#nullable enable
using System.Reflection;

namespace FluentDocker.Resources
{
  /// <summary>
  /// Identifies one embedded manifest resource resolved by a <see cref="ResourceQuery"/>: which
  /// assembly it lives in, its manifest namespace/filename split, and its location relative to the
  /// query root so it can be written back out under the original folder structure (see
  /// <c>ResourceExtensions.ToFile</c>).
  /// </summary>
  public sealed class ResourceInfo
  {
    /// <summary>The resource's filename (the part of the manifest name after its namespace), e.g. "config.json".</summary>
    public string Resource { get; set; } = null!;
    /// <summary>The full manifest namespace the resource is embedded under (excludes <see cref="Resource"/>).</summary>
    public string Namespace { get; set; } = null!;
    /// <summary>The namespace the originating <see cref="ResourceQuery"/> was rooted at.</summary>
    public string? Root { get; set; }
    /// <summary>
    /// The portion of <see cref="Namespace"/> below <see cref="Root"/>, dot-separated; empty when the
    /// resource sits directly at the root. Used to reconstruct subfolders when writing to disk.
    /// </summary>
    public string RelativeRootNamespace { get; set; } = null!;
    /// <summary>The assembly the resource is embedded in.</summary>
    public Assembly Assembly { get; set; } = null!;
  }
}
