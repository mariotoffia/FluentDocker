using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Resources;

namespace Ductus.FluentDocker.Extensions
{
	public static class ResourceExtensions
	{
#if !COREFX
		/// <summary>
		///   Queries for embedded resources from the <paramref name="assemblyAndNamespace" /> parameters
		///   <see cref="Assembly" /> and <see cref="Type.Namespace" />.
		/// </summary>
		/// <param name="assemblyAndNamespace">The assembly and namespace to query for resources</param>
		/// <param name="recursive">If the query should be namespace recursive or not (default true).</param>
		/// <returns>A enumeration of resources.</returns>
		public static IEnumerable<ResourceInfo> ResuorceQuery(this Type assemblyAndNamespace, bool recursive = true)
		{
			return
			  new ResourceQuery().From(assemblyAndNamespace.GetTypeInfo().Assembly.GetName().Name)
				.Namespace(assemblyAndNamespace.Namespace, recursive)
				.Query();
		}

		/// <summary>
		///   Extracts embedded resource based on the inparam <paramref name="assemblyAndNamespace" />,
		///   <see cref="Assembly" /> and <see cref="Type.Namespace" />.
		/// </summary>
		/// <param name="assemblyAndNamespace">The assemlby and namesspace to start searching for resources to extract.</param>
		/// <param name="targetPath">The target base filepath to start the extraction from.</param>
		/// <param name="files">
		///   Optional explicit files that are direct children of the <paramref name="assemblyAndNamespace" />
		///   namespace.
		/// </param>
		/// <remarks>
		///   This function extract recursively embedded resources if no <paramref name="files" /> has been specified. If any
		///   <paramref name="files" /> has been specifies it won't do a recurive extraction, instead all files in the provided
		///   namespace (in <paramref name="assemblyAndNamespace" />) will be matched against the <paramref name="files" />.
		/// </remarks>
		public static void ResourceExtract(this Type assemblyAndNamespace, TemplateString targetPath, params string[] files)
		{
			if (null == files || 0 == files.Length)
			{
				assemblyAndNamespace.ResuorceQuery().ToFile(targetPath);
				return;
			}

			new ResourceQuery().From(assemblyAndNamespace.GetTypeInfo().Assembly.GetName().Name)
			  .Namespace(assemblyAndNamespace.Namespace, false)
			  .Include(files)
			  .ToFile(targetPath);
		}
#endif

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
			new FileResourceWriter(targetPath).Write(
				new ResourceReader(new[]
				{
					new ResourceInfo
					{
						Assembly = GetAssembly(resource.Assembly),
						Namespace = resource.Namespace,
						RelativeRootNamespace = string.Empty,
						Resource = resource.Resource
					}
				}));

			return resource.Resource;
		}

		private static Assembly GetAssembly(string assemblyName)
		{
#if COREFX
			
			return GetAssemblies().First(x => x.GetName().Name == assemblyName);
		}

		private static IEnumerable<Assembly> GetAssemblies()
		{
			foreach (var library in Microsoft.Extensions.DependencyModel.DependencyContext.Default.RuntimeLibraries)
				yield return Assembly.Load(new AssemblyName(library.Name));
#else
			return AppDomain.CurrentDomain.GetAssemblies().First(x => x.GetName().Name == assemblyName);
#endif
		}
	}
}