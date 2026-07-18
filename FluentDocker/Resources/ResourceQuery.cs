#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentDocker.Common;

namespace FluentDocker.Resources
{
  /// <summary>
  /// Fluent builder that queries an assembly's embedded manifest resources under a namespace.
  /// Configure with <see cref="From"/>/<see cref="Namespace"/>/<see cref="Recursive"/>, then run the
  /// query with <see cref="Query"/> (everything under the namespace) or <see cref="Include"/> (only
  /// specific resource names).
  /// </summary>
  public sealed class ResourceQuery
  {
    private string? _assembly;
    private string _namespace = null!;
    private bool _recursive;

    /// <summary>Sets the assembly to search by simple name.</summary>
    /// <param name="assembly">
    /// The assembly's simple name (case-insensitive), resolved against the assemblies currently loaded
    /// in <see cref="AppDomain.CurrentDomain"/>. When <c>null</c> or empty, the query falls back to the
    /// caller's own assembly (see <see cref="Query"/>/<see cref="Include"/>).
    /// </param>
    /// <returns>This query, for chaining.</returns>
    public ResourceQuery From(string? assembly)
    {
      _assembly = assembly;
      return this;
    }

    /// <summary>Sets the root namespace to query resources under.</summary>
    /// <param name="ns">The namespace prefix embedded resources must fall under.</param>
    /// <param name="recursive">
    /// When <c>true</c> (default), also matches resources in sub-namespaces of <paramref name="ns"/>;
    /// when <c>false</c>, only resources embedded directly at <paramref name="ns"/> match.
    /// </param>
    /// <returns>This query, for chaining.</returns>
    public ResourceQuery Namespace(string ns, bool recursive = true)
    {
      _namespace = ns;
      _recursive = recursive;
      return this;
    }

    /// <summary>Enables recursive matching into sub-namespaces of the queried namespace.</summary>
    /// <returns>This query, for chaining.</returns>
    public ResourceQuery Recursive()
    {
      _recursive = true;
      return this;
    }

    /// <summary>Runs the query and returns every embedded resource matching the configured namespace.</summary>
    /// <remarks>
    /// Manifest resource names use dots for BOTH namespace separators and filename dots, so
    /// filenames are reconstructed heuristically (see the private <c>ExtractFile</c> notes).
    /// Two shapes remain ambiguous on this no-request path: a short (≤4 chars, dotless)
    /// filename in a sub-namespace (<c>Root.Sub.app</c>) extracts as a single file
    /// <c>Sub.app</c> at the target root rather than <c>Sub/app</c>, and a long dotless
    /// filename under a recursive query (<c>Root.Sub.README</c>) re-anchors to a root-level
    /// file <c>Sub.README</c>. When the on-disk name/location must be exact, use
    /// <see cref="Include"/> with the exact requested name — it rewrites the match from the
    /// request instead of relying on the heuristic.
    /// </remarks>
    /// <returns>The matching resources.</returns>
    /// <exception cref="FluentDockerException"><see cref="Namespace"/> was not called, or <see cref="From"/> named an assembly not currently loaded.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public IEnumerable<ResourceInfo> Query()
    {
      return QueryCore(ResolveAssembly(Assembly.GetCallingAssembly()));
    }

    /// <summary>
    /// Runs the query and returns only the resources matching one of <paramref name="resources"/>, by
    /// exact resource name, fully-qualified manifest name, or trailing-suffix (for multi-dot names like
    /// "Dockerfile.template" that a plain manifest-name split cannot disambiguate).
    /// </summary>
    /// <param name="resources">The requested resource names to match.</param>
    /// <returns>The matched resources, rewritten so each one's <see cref="ResourceInfo.Resource"/> is the requested name.</returns>
    /// <exception cref="FluentDockerException"><see cref="Namespace"/> was not called, or <see cref="From"/> named an assembly not currently loaded.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public IEnumerable<ResourceInfo> Include(params string[] resources)
    {
      return QueryCore(ResolveAssembly(Assembly.GetCallingAssembly()))
        .Select(x => MatchRequestedResource(x, resources))
        .OfType<ResourceInfo>();
    }

    /// <summary>
    /// Matches a resource against the requested names. An exact match (on <see cref="ResourceInfo.Resource"/>
    /// or the reconstructed fully qualified name) is returned unchanged. A trailing-suffix match is
    /// REWRITTEN onto the requested name instead of <see cref="ExtractFile"/>'s guess (Co-M1) — without
    /// this, a match like "Dockerfile.template" would extract as a mangled <c>Dockerfile/template</c>
    /// (directory + fragment) rather than the file <c>Dockerfile.template</c>.
    /// </summary>
    /// <remarks>
    /// This only ever runs on resources the caller's <see cref="QueryCore"/> query already yielded. A
    /// NON-recursive <see cref="Namespace"/> query drops a multi-dot resource (e.g.
    /// <c>Res.Dockerfile.template</c>, whose guessed sub-namespace <c>Res.Dockerfile</c> ≠ the query root
    /// <c>Res</c>) before it ever reaches this match — so the original MDL-MAJ-2 fix on this method alone
    /// did NOT make <c>Include("Dockerfile.template")</c> work; the resource was already gone (Co-H1). The
    /// caller must query recursively for the trailing-suffix match below to ever run.
    /// </remarks>
    private static ResourceInfo? MatchRequestedResource(ResourceInfo info, string[] requested)
    {
      foreach (var name in requested)
      {
        if (string.IsNullOrEmpty(name))
          continue;
        if (string.Equals(info.Resource, name, StringComparison.Ordinal))
          return info;

        // ns + "." + Resource reconstructs the original fully qualified manifest name.
        var fq = info.Namespace + "." + info.Resource;
        if (string.Equals(fq, name, StringComparison.Ordinal))
          return info;

        // Broaden to a trailing-suffix match only for MULTI-SEGMENT requests (which ExtractFile can
        // mangle, e.g. "Dockerfile.template"/"archive.tar.gz"). A bare extension like "json" has no dot
        // and must NOT match every *.json resource — it only matches an exact Resource name above.
        if (!name.Contains('.', StringComparison.Ordinal) || !fq.EndsWith("." + name, StringComparison.Ordinal))
          continue;

        // Suffix match: rewrite Namespace/Resource/RelativeRootNamespace from the REQUESTED name (not
        // ExtractFile's guess), reconstructing the folder from what's left of fq once the requested
        // name (and its separating dot) is removed, so the resource writes to the correct file at the
        // correct folder regardless of how ExtractFile originally split it (Co-M1).
        var prefix = fq[..(fq.Length - name.Length - 1)];

        // The requested name may overlap the NAMESPACE boundary (e.g. Namespace("Res.Sub") with
        // Include("Sub.x.y") against resource "Res.Sub.x.y"): the remaining prefix then falls
        // short of the query root. That is no match — never slice below Root (it would throw),
        // and never resolve a resource that sits outside the queried root.
        if (!string.Equals(prefix, info.Root, StringComparison.Ordinal) &&
            !prefix.StartsWith(info.Root + ".", StringComparison.Ordinal))
          continue;

        return new ResourceInfo
        {
          Assembly = info.Assembly,
          Namespace = prefix,
          Root = info.Root,
          RelativeRootNamespace = prefix == info.Root ? string.Empty : prefix[(info.Root!.Length + 1)..],
          Resource = name
        };
      }

      return null;
    }

    private Assembly ResolveAssembly(Assembly caller)
    {
      if (string.IsNullOrEmpty(_assembly))
        return caller;

      var loaded = AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(x => x.GetName().Name!.Equals(_assembly, StringComparison.OrdinalIgnoreCase));
      if (loaded != null)
        return loaded;

      // .NET loads assemblies lazily — the target may simply not be loaded yet when no type
      // from it has been touched (a startup-order heisenbug). Try an explicit load by simple
      // name before failing.
      try
      {
        return Assembly.Load(new AssemblyName(_assembly));
      }
      catch (Exception ex)
      {
        throw new FluentDockerException(
            $"Assembly '{_assembly}' was not found in the current AppDomain and could not be loaded.", ex);
      }
    }

    private IEnumerable<ResourceInfo> QueryCore(Assembly assembly)
    {
      if (_namespace == null)
        throw new FluentDockerException("Namespace not set. Call Namespace(...) before querying resources.");

      var namespacePrefix = _namespace + ".";
      foreach (var res in assembly.GetManifestResourceNames()
                   .Where(x => x.StartsWith(namespacePrefix, StringComparison.Ordinal)))
      {
        var file = ExtractFile(res);
        var ns = res[..(res.Length - file.Length - 1)];
        if (ns.Length < _namespace.Length)
        {
          continue;
        }

        // ExtractFile guessed a DOTLESS filename beyond the query root (e.g. "Res.Dockerfile.template"
        // -> "template"): a strong signal the true filename actually spans multiple dot-segments, since
        // ExtractFile only ever returns a dotless result when it couldn't find/trust an earlier dot. Best
        // effort for the RECURSIVE no-request Query()/ToFile() path (Include disambiguates exactly instead,
        // see MatchRequestedResource): re-anchor to the full remainder after the root so the resource lands
        // as one FILE at the target root instead of scattered into a wrong subfolder + fragment (Co-M1).
        // Gated to _recursive: on the non-recursive (root-level-only) path a genuinely-nested dotless file
        // (real folder Res/Sub/ with a file literally named "README" -> "Res.Sub.README") must stay
        // EXCLUDED, not be silently re-anchored to the root — re-anchoring there would reintroduce the
        // wrong-location silent-write class Co-H1 exists to close.
        if (_recursive && ns.Length > _namespace.Length && !file.Contains('.', StringComparison.Ordinal))
        {
          file = res[(_namespace.Length + 1)..];
          ns = _namespace;
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
    ///   Manifest resource names use dots as namespace separators, making original filenames
    ///   inherently lossy to reconstruct from this string alone: "Ns.Dockerfile.template" may be
    ///   reduced to "template", and "Ns.archive.tar.gz" may be reduced to "tar.gz" — this function
    ///   cannot tell a namespace-dot from a filename-dot with certainty. <see cref="QueryCore"/> applies
    ///   a best-effort refinement using the known query root for the no-request <see cref="Query"/> /
    ///   ToFile path, which resolves the common "long trailing segment" shape (e.g. ".template",
    ///   ".properties") but not every shape (a compound short extension like ".tar.gz" can still land
    ///   one folder off). Use <see cref="Include(string[])"/> with the exact requested name when the
    ///   result must be exact — it rewrites the match from the request instead of relying on this guess.
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
