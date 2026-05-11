using System;
using System.IO;
using FluentDocker.Extensions;
using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  /// <summary>
  /// This generates the _COPY_ command.
  /// </summary>
  /// <param name="url">The _URL_ to download the file from</param>
  /// <param name="from">The directory and filename where the file will be downloaded as.</param>
  /// <param name="to">To directory.</param>
  /// <param name="chownUserAndGroup">Optional --chown user:group.</param>
  /// <param name="fromAlias">
  /// Optional source location from earlier build stage FROM ... AS alias. This will
  /// generate --from=aliasname in the _COPY_ command and hence reference a earlier
  /// _FROM ... AS aliasname_ buildstep as source.
  /// </param>
  public sealed class CopyURLCommand(Uri url, TemplateString from, TemplateString to,
    TemplateString chownUserAndGroup = null, TemplateString fromAlias = null) : CopyCommand(from, to, chownUserAndGroup, fromAlias)
  {
    public Uri FromURL { get; } = url;
    public override string ToString()
    {
      return base.ToString();
    }
  }
}
