#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentDocker.Common;

namespace FluentDocker.Resources
{
  public sealed class ResourceQuery
  {
    private string? _assembly;
    private string _namespace = null!;
    private bool _recursive;

    public ResourceQuery From(string? assembly)
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    public IEnumerable<ResourceInfo> Query()
    {
      return QueryCore(ResolveAssembly(Assembly.GetCallingAssembly()));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public IEnumerable<ResourceInfo> Include(params string[] resources)
    {
      return QueryCore(ResolveAssembly(Assembly.GetCallingAssembly()))
        .Where(x => resources.Contains(x.Resource));
    }

    private Assembly ResolveAssembly(Assembly caller)
    {
      if (string.IsNullOrEmpty(_assembly))
        return caller;

      return AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(x => x.GetName().Name!.Equals(_assembly, StringComparison.OrdinalIgnoreCase))
        ?? throw new FluentDockerException($"Assembly '{_assembly}' was not found in the current AppDomain.");
    }

    private IEnumerable<ResourceInfo> QueryCore(Assembly assembly)
    {
      foreach (var res in assembly.GetManifestResourceNames()
                   .Where(x => x.StartsWith(_namespace, StringComparison.Ordinal)))
      {
        var file = ExtractFile(res);
        var ns = res[..(res.Length - file.Length - 1)];
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
          RelativeRootNamespace = nseqlen ? string.Empty : ns[(_namespace.Length + 1)..],
          Resource = file
        };
      }
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
