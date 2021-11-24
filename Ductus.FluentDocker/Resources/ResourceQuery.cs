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
        ? Assembly.GetCallingAssembly() :
        AppDomain.CurrentDomain.GetAssemblies()
        .First(x => x.GetName().Name.Equals(_assembly, StringComparison.OrdinalIgnoreCase));

      var q = assembly.GetManifestResourceNames();


      foreach (var res in assembly.GetManifestResourceNames().Where(x => x.StartsWith(_namespace)))
      {
        var file = ExtractFile(res);
        var ns = res.Substring(0, res.Length - file.Length - 1);
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
          Root = _namespace,
          RelativeRootNamespace = nseqlen ? string.Empty : ns.Substring(_namespace.Length + 1),
          Resource = file
        };
      }
    }

    public IEnumerable<ResourceInfo> Include(params string[] resources)
    {
      return Query().Where(x => resources.Contains(x.Resource));
    }

    /// <summary>
    ///   TODO: Ugly hack since GetManifestResourceInfo(res) do not work!
    /// </summary>
    /// <param name="fqResource">The fully qualified file including namespace.</param>
    /// <returns>Probably a filename.</returns>
    private static string ExtractFile(string fqResource)
    {
      var last = fqResource.LastIndexOf(".", StringComparison.Ordinal);
      if (-1 == last)
      {
        return fqResource;
      }

      var len = fqResource.Length - last;
      if (len > 5)
      {
        // Assume "dotless" file
        return fqResource.Substring(last + 1);
      }

      for (var i = fqResource.Length - len - 1; i >= 0; i--)
      {
        if (fqResource[i] == '.')
        {
          return fqResource.Substring(i + 1);
        }
      }

      return fqResource;
    }
  }
}
