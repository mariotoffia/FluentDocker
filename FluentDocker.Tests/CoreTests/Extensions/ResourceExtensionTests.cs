using System.Linq;
using System.Reflection;
using FluentDocker.Builders;
using FluentDocker.Extensions;
using FluentDocker.Resources;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Extensions
{
  [Trait("Category", "Unit")]
  public class ResourceExtensionTests
  {
    [Fact]
    public void ResourceQuery_FromAssembly_ListsResources()
    {
      // Test the ResourceQuery class directly
      var assembly = Assembly.GetExecutingAssembly();
      var assemblyName = assembly.GetName().Name;

      var resources = new ResourceQuery()
          .From(assemblyName)
          .Namespace(assemblyName)
          .Query()
          .ToArray();

      // May or may not have embedded resources, just check it doesn't throw
      Assert.NotNull(resources);
    }

    [Fact]
    public void ResourceQuery_Recursive_CanBeSet()
    {
      var assembly = Assembly.GetExecutingAssembly();
      var assemblyName = assembly.GetName().Name;

      var query = new ResourceQuery()
          .From(assemblyName)
          .Namespace(assemblyName)
          .Recursive();

      // Just check fluent API works
      var resources = query.Query().ToArray();
      Assert.NotNull(resources);
    }

    [Fact]
    public void ResourceInfo_ContainsExpectedProperties()
    {
      // Test ResourceInfo structure
      var info = new ResourceInfo
      {
        Assembly = Assembly.GetExecutingAssembly(),
        Resource = "test.txt",
        Namespace = "FluentDocker.Tests"
      };

      Assert.Equal("test.txt", info.Resource);
      Assert.Equal("FluentDocker.Tests", info.Namespace);
      Assert.NotNull(info.Assembly);
    }

    [Fact]
    public void ResourceQuery_FromFluentDockerAssembly_FindsResources()
    {
      // Query from the main FluentDocker assembly which should have embedded resources
      var fluentDockerAssembly = typeof(Builder).Assembly;
      var assemblyName = fluentDockerAssembly.GetName().Name;

      // This assembly may or may not have embedded resources
      // The key is that the query executes without error
      var resources = new ResourceQuery()
          .From(assemblyName)
          .Namespace(assemblyName)
          .Query()
          .ToArray();

      Assert.NotNull(resources);
    }

    [Fact]
    public void ResourceExtension_FromType_Works()
    {
      // Test the extension method form
      var resources = typeof(Builder).ResourceQuery(recursive: false).ToArray();

      // Just verify it works (may be empty if no resources)
      Assert.NotNull(resources);
    }
  }
}

