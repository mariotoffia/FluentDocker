using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace FluentDocker.Resources
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
    ///   Extracts the filename from a fully qualified manifest resource name.
    /// </summary>
    /// <remarks>
    ///   .NET's GetManifestResourceInfo returns metadata about the resource's location
    ///   (embedded, linked, satellite assembly) but not the original filename.
    ///   Manifest resource names use dots as namespace separators, making it impossible
    ///   to distinguish between "Namespace.File.txt" (file: File.txt) and
    ///   "Namespace.File.Name.txt" (file: Name.txt). This method uses heuristics:
    ///   - If extension > 5 chars, assume the file has no extension (dotless file)
    ///   - Otherwise, walk backward to find the filename (everything after the last namespace dot)
    /// </remarks>
    /// <param name="fqResource">The fully qualified resource name including namespace (e.g., "MyApp.Resources.config.json").</param>
    /// <returns>The extracted filename (e.g., "config.json").</returns>
    private static string ExtractFile(string fqResource)
    {
      var extensionDot = fqResource.LastIndexOf('.');
      if (extensionDot == -1)
        return fqResource;

      var extensionLength = fqResource.Length - extensionDot;

      // If "extension" is longer than 5 chars, it's likely a dotless filename
      // (e.g., "Namespace.Dockerfile" where "Dockerfile" has no extension)
      if (extensionLength > 5)
        return fqResource[(extensionDot + 1)..];

      // Walk backward from extension dot to find the filename start (previous dot = namespace separator)
      for (var i = extensionDot - 1; i >= 0; i--)
      {
        if (fqResource[i] == '.')
          return fqResource[(i + 1)..];
      }

      return fqResource;
    }
  }
}
