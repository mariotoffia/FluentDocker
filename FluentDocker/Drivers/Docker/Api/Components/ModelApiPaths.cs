using System;
using System.Text;
using FluentDocker.Model.Models;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Builds safe URL paths for the native <c>/models/*</c> API. The native endpoint
  /// addresses a Docker Hub model by its <c>{namespace}/{name}</c> repository, so this
  /// helper escapes and validates every path segment — a crafted reference cannot
  /// escape or traverse the path — and refuses references it cannot faithfully
  /// represent (registry-qualified, digest-pinned, or a specific non-default tag) so a
  /// caller is never silently pointed at the wrong model.
  /// </summary>
  public static class ModelApiPaths
  {
    /// <summary>
    /// Builds the <c>/models/{namespace}/{name}</c> path for <paramref name="model"/>.
    /// </summary>
    /// <param name="model">The model reference.</param>
    /// <returns>The escaped native API path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <exception cref="NotSupportedException">
    /// The reference is registry-qualified, digest-pinned, or carries a specific
    /// (non-<c>latest</c>) tag — none of which the native repository path can target.
    /// </exception>
    /// <exception cref="ArgumentException">A reference segment is not a safe path segment.</exception>
    public static string ForModel(ModelReference model)
    {
      ArgumentNullException.ThrowIfNull(model);

      if (!string.IsNullOrEmpty(model.Registry))
        throw new NotSupportedException(
            $"The native /models API addresses Docker Hub models by '{{namespace}}/{{name}}'; the " +
            $"registry-qualified reference '{model}' is not supported here. Use the CLI management driver.");

      if (!string.IsNullOrEmpty(model.Digest) ||
          (!string.IsNullOrEmpty(model.Tag) && !string.Equals(model.Tag, "latest", StringComparison.Ordinal)))
        throw new NotSupportedException(
            $"The native /models API addresses a model by repository; the digest/tag in '{model}' cannot " +
            $"be targeted here. Use the CLI management driver for version-qualified references.");

      var sb = new StringBuilder("/models");
      AppendSegment(sb, model.Namespace);
      AppendSegment(sb, model.Name);
      return sb.ToString();
    }

    /// <summary>
    /// True when <paramref name="segment"/> is a safe single URL path segment: not
    /// empty, not <c>.</c>/<c>..</c>, and free of path separators, query/fragment
    /// markers, percent-encoding, and control characters.
    /// </summary>
    /// <param name="segment">The candidate segment.</param>
    /// <returns><c>true</c> when safe.</returns>
    public static bool IsSafeSegment(string segment)
    {
      if (string.IsNullOrEmpty(segment) || segment == "." || segment == "..")
        return false;

      foreach (var c in segment)
      {
        if (c is '/' or '\\' or '?' or '#' or '%' || c < 0x20 || c == 0x7f)
          return false;
      }

      return true;
    }

    private static void AppendSegment(StringBuilder sb, string part)
    {
      if (string.IsNullOrEmpty(part))
        return;

      if (!IsSafeSegment(part))
        throw new ArgumentException($"Model reference segment '{part}' is not a valid URL path segment.");

      sb.Append('/').Append(Uri.EscapeDataString(part));
    }
  }
}
