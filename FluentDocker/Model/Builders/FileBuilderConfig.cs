#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders
{
  /// <summary>
  /// Accumulated state for a <c>DockerfileBuilder</c> session: either a rendered Dockerfile string,
  /// a path to an existing Dockerfile, or a queued list of instruction commands to render one.
  /// </summary>
  public sealed class FileBuilderConfig
  {
    /// <summary>The fully rendered Dockerfile contents, once built; <c>null</c> until rendered.</summary>
    public string? DockerFileString { get; set; }
    /// <summary>When set, an existing Dockerfile to use instead of rendering <see cref="Commands"/>.</summary>
    public TemplateString? UseFile { get; set; }
    /// <summary>The queued Dockerfile instructions, in the order they will be rendered.</summary>
    public IList<ICommand> Commands { get; } = [];

    /// <summary>Renders <see cref="Commands"/> as a Dockerfile, one instruction per line.</summary>
    public override string ToString()
    {
      var sb = new StringBuilder();
      foreach (var cmd in Commands)
      {
        sb.AppendLine(cmd.ToString());
      }
      return sb.ToString();
    }
  }
}
