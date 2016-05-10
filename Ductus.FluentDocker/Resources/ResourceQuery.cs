using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Ductus.FluentDocker.Resources
{
  public sealed class ResourceQuery
  {
    private string _assembly;
    private string _namespace;
    private bool _recursive;

    public ResourceQuery From(string assembly)
    {
      _assembly = assembly;
      return this;
    }

    public ResourceQuery Namespace(string ns, bool recursive = true)
    {
      _namespace = ns;
      _recursive = recursive;
      return this;
    }

    public ResourceQuery Recursive()
    {
      _recursive = true;
      return this;
    }

    public IEnumerable<ResourceInfo> Query()
    {
      var assembly = string.IsNullOrEmpty(_assembly)
        ? Assembly.GetCallingAssembly()
        : AppDomain.CurrentDomain.GetAssemblies()
          .First(x => x.GetName().Name.Equals(_assembly, StringComparison.OrdinalIgnoreCase));

      foreach (var res in assembly.GetManifestResourceNames().Where(x => x.StartsWith(_namespace)))
      {
        var resInfo = assembly.GetManifestResourceInfo(res);
        if (null == resInfo)
        {
          continue;
        }

        var ns = res.Substring(0, res.Length - resInfo.FileName.Length);
        if (ns.Length < _namespace.Length)
        {
          continue;
        }

        var nseqlen = ns.Length == _namespace.Length;
        if (!_recursive)
        {
          if (!nseqlen)
          {
            continue;
          }
        }

        yield return new ResourceInfo
        {
          Assembly = assembly,
          Namespace = ns,
          RelativeNamespace = nseqlen ? string.Empty : ns.Substring(_namespace.Length),
          Resource = resInfo.FileName
        };
      }
    }

    public IEnumerable<ResourceInfo> Include(params string[] resources)
    {
      return Query().Where(x => resources.Contains(x.Resource));
    }
  }
}