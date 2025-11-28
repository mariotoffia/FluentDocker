using System.Collections.Generic;
using System.Text;
using FluentDocker.Extensions;
using FluentDocker.Model.Containers;

namespace FluentDocker.Model.Commands
{
  /// <summary>
  /// Arguments for docker image rm command.
  /// </summary>
  public struct ImageRmCommandArgs
  {
    /// <summary>Image IDs or names to remove.</summary>
    public IList<string> ImageIds { get; set; }
    /// <summary>Force removal of the image.</summary>
    public bool Force { get; set; }
    /// <summary>Do not delete untagged parents.</summary>
    public bool NoPrune { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Force)
        sb.Append(" --force");
      if (NoPrune)
        sb.Append(" --no-prune");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker images command (list images).
  /// </summary>
  public struct ImagesListCommandArgs
  {
    /// <summary>Show all images (default hides intermediate images).</summary>
    public bool All { get; set; }
    /// <summary>Show digests.</summary>
    public bool Digests { get; set; }
    /// <summary>Filter output based on conditions provided.</summary>
    public string[] Filters { get; set; }
    /// <summary>Format the output using a Go template.</summary>
    public string Format { get; set; }
    /// <summary>Don't truncate output.</summary>
    public bool NoTrunc { get; set; }
    /// <summary>Only show image IDs.</summary>
    public bool Quiet { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (All)
        sb.Append(" --all");
      if (Digests)
        sb.Append(" --digests");
      sb.OptionIfExists("--filter=", Filters);
      sb.OptionIfExists("--format ", Format);
      if (NoTrunc)
        sb.Append(" --no-trunc");
      if (Quiet)
        sb.Append(" --quiet");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker image pull command.
  /// </summary>
  public struct ImagePullCommandArgs
  {
    /// <summary>The image name to pull (with optional tag/digest).</summary>
    public string Image { get; set; }
    /// <summary>Download all tagged images in the repository.</summary>
    public bool AllTags { get; set; }
    /// <summary>Skip image verification.</summary>
    public bool DisableContentTrust { get; set; }
    /// <summary>Set platform if server is multi-platform capable.</summary>
    public string Platform { get; set; }
    /// <summary>Suppress verbose output.</summary>
    public bool Quiet { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (AllTags)
        sb.Append(" --all-tags");
      if (DisableContentTrust)
        sb.Append(" --disable-content-trust");
      sb.OptionIfExists("--platform ", Platform);
      if (Quiet)
        sb.Append(" --quiet");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker image push command.
  /// </summary>
  public struct ImagePushCommandArgs
  {
    /// <summary>The image name to push (with optional tag).</summary>
    public string Image { get; set; }
    /// <summary>Push all tagged images in the repository.</summary>
    public bool AllTags { get; set; }
    /// <summary>Skip image signing.</summary>
    public bool DisableContentTrust { get; set; }
    /// <summary>Suppress verbose output.</summary>
    public bool Quiet { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (AllTags)
        sb.Append(" --all-tags");
      if (DisableContentTrust)
        sb.Append(" --disable-content-trust");
      if (Quiet)
        sb.Append(" --quiet");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker image tag command.
  /// </summary>
  public struct ImageTagCommandArgs
  {
    /// <summary>Source image (name:tag or ID).</summary>
    public string SourceImage { get; set; }
    /// <summary>Target image (name:tag).</summary>
    public string TargetImage { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
  }

  /// <summary>
  /// Arguments for docker image inspect command.
  /// </summary>
  public struct ImageInspectCommandArgs
  {
    /// <summary>Image IDs or names to inspect.</summary>
    public IList<string> ImageIds { get; set; }
    /// <summary>Format the output using a Go template.</summary>
    public string Format { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--format ", Format);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker image history command.
  /// </summary>
  public struct ImageHistoryCommandArgs
  {
    /// <summary>The image name or ID.</summary>
    public string Image { get; set; }
    /// <summary>Format the output using a Go template.</summary>
    public string Format { get; set; }
    /// <summary>Print sizes and dates in human readable format.</summary>
    public bool Human { get; set; }
    /// <summary>Don't truncate output.</summary>
    public bool NoTrunc { get; set; }
    /// <summary>Only show image IDs.</summary>
    public bool Quiet { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--format ", Format);
      if (Human)
        sb.Append(" --human");
      if (NoTrunc)
        sb.Append(" --no-trunc");
      if (Quiet)
        sb.Append(" --quiet");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker image save command.
  /// </summary>
  public struct ImageSaveCommandArgs
  {
    /// <summary>Image names or IDs to save.</summary>
    public IList<string> Images { get; set; }
    /// <summary>Write to a file, instead of STDOUT.</summary>
    public string Output { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("-o ", Output);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker image load command.
  /// </summary>
  public struct ImageLoadCommandArgs
  {
    /// <summary>Read from tar archive file.</summary>
    public string Input { get; set; }
    /// <summary>Suppress the load output.</summary>
    public bool Quiet { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("-i ", Input);
      if (Quiet)
        sb.Append(" --quiet");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker image import command.
  /// </summary>
  public struct ImageImportCommandArgs
  {
    /// <summary>File/URL/- to read the source from.</summary>
    public string Source { get; set; }
    /// <summary>Repository name for the new image.</summary>
    public string Repository { get; set; }
    /// <summary>Tag for the new image.</summary>
    public string Tag { get; set; }
    /// <summary>Apply Dockerfile instruction to the created image.</summary>
    public string[] Changes { get; set; }
    /// <summary>Set commit message for imported image.</summary>
    public string Message { get; set; }
    /// <summary>Set platform if server is multi-platform capable.</summary>
    public string Platform { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--change ", Changes);
      sb.OptionIfExists("--message ", Message);
      sb.OptionIfExists("--platform ", Platform);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker image prune command.
  /// </summary>
  public struct ImagePruneCommandArgs
  {
    /// <summary>Remove all unused images, not just dangling ones.</summary>
    public bool All { get; set; }
    /// <summary>Provide filter values.</summary>
    public string[] Filters { get; set; }
    /// <summary>Do not prompt for confirmation.</summary>
    public bool Force { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (All)
        sb.Append(" --all");
      sb.OptionIfExists("--filter ", Filters);
      if (Force)
        sb.Append(" --force");

      return sb.ToString();
    }
  }
}

