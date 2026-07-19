using System;
using System.Linq;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// Parallel-safe naming helpers for <see cref="ResourceBase"/> derivatives.
  /// Split out purely to keep each source file within the repository's 500-line limit.
  /// </summary>
  public abstract partial class ResourceBase
  {
    /// <summary>
    /// Generates a unique name for parallel-safe resource creation.
    /// </summary>
    protected static string GenerateUniqueName(string prefix)
    {
      const int maxDockerNameLength = 63;
      const int guidLength = 32;
      var maxPrefixLength = maxDockerNameLength - guidLength - 1;
      var safePrefix = prefix.Length <= maxPrefixLength
          ? prefix
          : prefix[..maxPrefixLength];
      return $"{safePrefix}-{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Appends a short, deterministic session suffix to a caller-supplied resource name so that
    /// parallel runs cannot collide on it. Used for resources that carry a fixed caller name and
    /// cannot be tagged with session labels (swarm stacks, kube plays). Returns
    /// <paramref name="baseName"/> unchanged when <paramref name="sessionId"/> is empty or has no
    /// name-safe characters. The same session id always yields the same name so deploy and teardown
    /// target the same resource.
    /// </summary>
    protected static string SessionScopedName(string baseName, string sessionId)
    {
      if (string.IsNullOrEmpty(sessionId))
        return baseName;

      var suffix = new string(sessionId.Where(char.IsLetterOrDigit).Take(12).ToArray());
      return suffix.Length == 0 ? baseName : $"{baseName}-{suffix}";
    }
  }
}
