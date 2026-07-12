#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentDocker.Common;
using FluentDocker.Model.Common;
using FluentDocker.Resources;

namespace FluentDocker.Extensions
{
  /// <summary>
  /// Convenience entry points over <see cref="FluentDocker.Resources.ResourceQuery"/>/<see cref="FluentDocker.Resources.FileResourceWriter"/>
  /// for querying and extracting a type's embedded resources.
  /// </summary>
  public static class ResourceExtensions
  {
    /// <summary>
    ///   Queries for embedded resources from the <paramref name="assemblyAndNamespace" /> parameters
    ///   <see cref="Assembly" /> and <see cref="Type.Namespace" />.
    /// </summary>
    /// <param name="assemblyAndNamespace">The assembly and namespace to query for resources</param>
    /// <param name="recursive">If the query should be namespace recursive or not (default true).</param>
    /// <returns>A enumeration of resources.</returns>
    public static IEnumerable<ResourceInfo> ResourceQuery(this Type assemblyAndNamespace, bool recursive = true)
    {
      return
        new ResourceQuery().From(assemblyAndNamespace.GetTypeInfo().Assembly.GetName().Name!)
        .Namespace(assemblyAndNamespace.Namespace!, recursive)
        .Query();
    }

    /// <summary>
    ///   Extracts embedded resource based on the inparam <paramref name="assemblyAndNamespace" />,
    ///   <see cref="Assembly" /> and <see cref="Type.Namespace" />.
    /// </summary>
    /// <param name="assemblyAndNamespace">The assembly and namespace to start searching for resources to extract.</param>
    /// <param name="targetPath">The target base filepath to start the extraction from.</param>
    /// <param name="files">
    ///   Optional explicit files that are direct children of the <paramref name="assemblyAndNamespace" />
    ///   namespace.
    /// </param>
    /// <remarks>
    ///   This function extracts recursively in both cases. If no <paramref name="files" /> has been specified,
    ///   every embedded resource under the <paramref name="assemblyAndNamespace" /> namespace (and its
    ///   sub-namespaces) is written out. If any <paramref name="files" /> has been specified, the query still
    ///   searches recursively — a multi-dot filename (e.g. "Dockerfile.template") is embedded one namespace
    ///   segment "deeper" than its manifest name suggests, so a non-recursive query would drop it before it
    ///   could ever be matched — but only resources matching one of the <paramref name="files" /> (via
    ///   <see cref="ResourceQuery.Include" />) are extracted, written under the requested name.
    /// </remarks>
    public static void ResourceExtract(this Type assemblyAndNamespace, TemplateString targetPath, params string[] files)
    {
      if (null == files || 0 == files.Length)
      {
        assemblyAndNamespace.ResourceQuery().ToFile(targetPath);
        return;
      }

      new ResourceQuery().From(assemblyAndNamespace.GetTypeInfo().Assembly.GetName().Name!)
        .Namespace(assemblyAndNamespace.Namespace!, true)
        .Include(files)
        .ToFile(targetPath);
    }

    /// <summary>
    ///   Writes a set of resources using a base filepath in inparameter <paramref name="targetPath" />.
    /// </summary>
    /// <param name="resources">The resources to be written.</param>
    /// <param name="targetPath">The target base path to write the <paramref name="resources" /> to.</param>
    /// <remarks>
    ///   If the <see cref="ResourceInfo.RelativeRootNamespace" /> is set it will be regarded as subfolders to
    ///   the <paramref name="targetPath" />.
    /// </remarks>
    public static void ToFile(this IEnumerable<ResourceInfo> resources, TemplateString targetPath)
    {
      new FileResourceWriter(targetPath).Write(new ResourceReader(resources));
    }

    /// <summary>
    ///   Writes a resource expressed in the <paramref name="resource" /> onto the <paramref name="targetPath" />.
    /// </summary>
    /// <param name="resource">The embedded resource to be extracted.</param>
    /// <param name="targetPath">The directory path to where the resource will be written.</param>
    /// <returns>The resource name (without any path) written.</returns>
    public static string ToFile(this EmbeddedUri resource, TemplateString targetPath)
    {
      if (string.IsNullOrWhiteSpace(resource.Resource))
        throw new FluentDockerException($"Embedded resource URI '{resource}' must include a resource segment.");

      var resourceName = resource.Resource;
      new FileResourceWriter(targetPath).Write(
        new ResourceReader(
        [
          new ResourceInfo
          {
            Assembly = GetAssembly(resource.Assembly),
            Namespace = resource.Namespace,
            RelativeRootNamespace = string.Empty,
            Resource = resourceName
          }
        ]));

      return resourceName;
    }

    private static Assembly GetAssembly(string assemblyName)
    {
      return AppDomain.CurrentDomain.GetAssemblies()
          .FirstOrDefault(x => x.GetName().Name!.Equals(assemblyName, StringComparison.OrdinalIgnoreCase))
          ?? throw new FluentDockerException($"Assembly '{assemblyName}' was not found in the current AppDomain.");
    }
  }
}
