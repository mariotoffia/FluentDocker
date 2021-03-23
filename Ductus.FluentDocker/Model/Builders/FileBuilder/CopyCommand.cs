using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;

namespace Ductus.FluentDocker.Model.Builders.FileBuilder
{
  public sealed class CopyCommand : ICommand
  {
    /// <summary>
    /// This generates the _COPY_ command.
    /// </summary>
    /// <param name="from">From directory.</param>
    /// <param name="to">To directory.</param>
    /// <param name="chownUserAndGroup">Optional --chown user:group.</param>
    /// <param name="fromAlias">
    /// Optional source location from earlier buildstage FROM ... AS alias. This will 
    /// generate --from=aliasname in the _COPY_ command and hence reference a earlier
    /// _FROM ... AS aliasname_ buildstep as source.
    /// </param>
    public CopyCommand(TemplateString from, TemplateString to, 
      TemplateString chownUserAndGroup = null, TemplateString fromAlias = null)
    {
      From = from.Rendered;
      To = to.Rendered;

      if (null != chownUserAndGroup && !string.IsNullOrEmpty(chownUserAndGroup.Rendered)) {
        Chown = chownUserAndGroup.Rendered;
      }

      if (null != fromAlias && !string.IsNullOrEmpty(fromAlias.Rendered)) {
        Alias = fromAlias.Rendered;
      }
    }

    public string From { get; }
    public string To { get; }
    public string Alias { get; }
    public string Chown { get; }

    public override string ToString()
    {
      string s = "COPY";

      if (!string.IsNullOrEmpty(Chown)) {
        s = $"{s} --chown={Chown}";
      }

      if (!string.IsNullOrEmpty(Alias)) {
        s = $"{s} --from={Alias}";
      }

      return $"{s} [{From},{To}]";
    }
  }
}
