using System;

namespace Ductus.FluentDocker.Model.Volumes
{
    public sealed class Volume
    {
      public DateTime Created { get; set; }
      public string Driver { get; set; }
      //public object Labels { get; set; }
      public string Name { get; set; }
      public string Scope { get; set; }
      //public object Options { get; set; }
    }
}
