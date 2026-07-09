using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Container builder — force-pull handling and image-reference parsing.
  /// </summary>
  internal sealed partial class ContainerBuilder
  {
    /// <summary>
    /// Pulls the configured image when <c>ForcePullImage()</c> was requested. The image
    /// reference is parsed so an already-tagged or digest-pinned ref is preserved instead of
    /// being clobbered with <c>latest</c>, and a pull failure surfaces as a
    /// <see cref="DriverException"/> rather than being silently swallowed.
    /// </summary>
    private async Task ExecuteForcePullAsync(
        IImageDriver imageDriver, DriverContext context, CancellationToken cancellationToken)
    {
      var (image, tag) = ParseImageReference(_image);

      var response = await imageDriver
          .PullAsync(context, image, tag, null, cancellationToken)
          .ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to pull image '{_image}': {response.Error}",
            ErrorCodes.Image.PullFailed,
            response.ErrorContext);
      }
    }

    /// <summary>
    /// Splits an image reference into the value passed as the driver <c>image</c> argument and
    /// the value passed as the <c>tag</c> argument. A registry host:port colon (e.g.
    /// <c>myregistry:5000/img</c>) is not mistaken for a tag, a digest (<c>@sha256:...</c>) is
    /// preserved by deferring to the driver's as-is handling, and <c>latest</c> is only used
    /// when the reference carries neither a tag nor a digest.
    /// </summary>
    internal static (string image, string tag) ParseImageReference(string reference)
    {
      // Validate() already rejects a null/empty _image before ForcePull runs; guard whitespace
      // too so a blank ref is passed verbatim (tag:null) and never becomes a ":latest" pull.
      if (string.IsNullOrWhiteSpace(reference))
        return (reference, null);

      // Digest-pinned reference (repo@sha256:...). Pass the full ref and tag:null so the driver
      // uses it verbatim instead of appending a tag.
      if (reference.Contains('@'))
        return (reference, null);

      // A ':' denotes a tag only when no '/' follows it; otherwise it is a registry host:port.
      var lastColon = reference.LastIndexOf(':');
      if (lastColon > 0 && reference.IndexOf('/', lastColon) < 0)
      {
        var tag = reference[(lastColon + 1)..];
        if (string.IsNullOrWhiteSpace(tag))
          throw new FluentDockerException(
              $"Image reference '{reference}' has an empty tag. Specify a tag after ':' or omit the colon.");
        return (reference[..lastColon], tag);
      }

      return (reference, "latest");
    }
  }
}
