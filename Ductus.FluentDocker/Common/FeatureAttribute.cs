using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Ductus.FluentDocker.Model;

namespace Ductus.FluentDocker.Common
{
  [AttributeUsage(AttributeTargets.Class)]
  public sealed class FeatureAttribute : Attribute
  {
    public FeatureAttribute()
    {
      Dependencies = new Type[0];
    }

    public FeatureAttribute(string id, IEnumerable<Type> dependencies = null)
    {
      Id = id;
      Dependencies = dependencies;
    }

    public string Id { get; set; }
    public IEnumerable<Type> Dependencies { get; set; }

    public void Validate()
    {
      if (null == Id) throw new FluentDockerException("A feature must have a valid Id");

      foreach (var dependency in Dependencies)
        if (!dependency.GetInterfaces().Contains(typeof(IFeature)))
          throw new FluentDockerException($"Feature {Id} dependency is dependant on non IFeature type ({dependency})." +
                                          " This is no allowed");
    }
  }
}