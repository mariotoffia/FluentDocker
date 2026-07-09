#nullable enable
using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  /// <summary>Represents a Dockerfile <c>COPY</c> instruction.</summary>
  public class CopyCommand : ICommand
  {
    /// <summary>
    /// This generates the _COPY_ command.
    /// </summary>
    /// <param name="from">From directory.</param>
    /// <param name="to">To directory.</param>
    /// <param name="chownUserAndGroup">Optional --chown user:group.</param>
    /// <param name="fromAlias">
    /// Optional source location from earlier build stage FROM ... AS alias. This will
    /// generate --from=aliasname in the _COPY_ command and hence reference a earlier
    /// _FROM ... AS aliasname_ buildstep as source.
    /// </param>
    public CopyCommand(TemplateString from, TemplateString to,
      TemplateString? chownUserAndGroup = null, TemplateString? fromAlias = null)
    {
      From = from.Rendered;
      To = to.Rendered;

      if (null != chownUserAndGroup && !string.IsNullOrEmpty(chownUserAndGroup.Rendered))
      {
        Chown = DockerfileInstructionGuard.ValidateToken(
            chownUserAndGroup.Rendered, "COPY", "chown");
      }

      if (null != fromAlias && !string.IsNullOrEmpty(fromAlias.Rendered))
      {
        Alias = DockerfileInstructionGuard.ValidateToken(
            fromAlias.Rendered, "COPY", "from alias");
      }
    }

    /// <summary>Gets the source path.</summary>
    public string From { get; internal set; }
    /// <summary>Gets the destination path.</summary>
    public string To { get; }
    /// <summary>Gets the optional source build stage alias.</summary>
    public string? Alias { get; }
    /// <summary>Gets the optional owner assigned by <c>--chown</c>.</summary>
    public string? Chown { get; }

    /// <summary>Renders the instruction.</summary>
    public override string ToString()
    {
      var s = "COPY";

      if (!string.IsNullOrEmpty(Chown))
      {
        s = $"{s} --chown={Chown}";
      }

      if (!string.IsNullOrEmpty(Alias))
      {
        s = $"{s} --from={Alias}";
      }

      return $"{s} {DockerfileJson.Array([NormalizePath(From), NormalizePath(To)])}";
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
  }
}
