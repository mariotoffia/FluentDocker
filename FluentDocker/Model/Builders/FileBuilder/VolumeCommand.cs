using System;
using System.Collections.Generic;
using System.Linq;
using FluentDocker.Extensions;
using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  public sealed class VolumeCommand : ICommand
  {
    public VolumeCommand(params TemplateString[] mountpoints)
    {
      var list = new List<string>();

      foreach (var s in mountpoints.Select(s => s.Rendered))
      {
        list.Add(s.WrapWithChar("\""));
      }

      Mountpoints = list.ToArray();
    }

    public string[] Mountpoints { get; }

    public override string ToString()
    {
      return $"VOLUME [\"{string.Join(",", Mountpoints)}\"]";
    }
  }
}
