using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Resources;

namespace Ductus.FluentDocker.Extensions
{
  public static class ResourceExtensions
  {
    public static void Extract(this Assembly asm, string ns, TemplateString targetPath, params string[] files)
    {
      if (null == files || 0 == files.Length)
      {
        new ResourceQuery().Namespace(ns).Query().ToFile(targetPath);
        return;
      }

      new ResourceQuery().Namespace(ns, false).Include(files).ToFile(targetPath);
    }

    public static void ToFile(this IEnumerable<ResourceInfo> resources, TemplateString targetPath)
    {
      new FileResourceWriter(targetPath).Write(new ResourceReader(resources));
    }

    public static string ToFile(this EmbeddedUri resource, TemplateString targetPath)
    {
      new FileResourceWriter(targetPath).Write(
        new ResourceReader(new[]
        {
          new ResourceInfo
          {
            Assembly = AppDomain.CurrentDomain.GetAssemblies().First(x => x.GetName().Name == resource.Assembly),
            Namespace = resource.Namespace,
            RelativeNamespace = string.Empty,
            Resource = resource.Resource
          }
        }));
      return resource.Resource;
    }
  }
}