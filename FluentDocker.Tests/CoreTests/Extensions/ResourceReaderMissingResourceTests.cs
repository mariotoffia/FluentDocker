using System.IO;
using System.Linq;
using FluentDocker.Common;
using FluentDocker.Resources;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Extensions
{
  [Trait("Category", "Unit")]
  public class ResourceReaderMissingResourceTests
  {
    [Fact]
    public void EnumeratingMissingManifestResource_ThrowsFluentDockerExceptionNamingResource()
    {
      var resource = new ResourceInfo
      {
        Assembly = typeof(ResourceReaderMissingResourceTests).Assembly,
        Namespace = "FluentDocker.Tests.Fixtures.Dmr",
        Resource = "missing-resource.json",
        Root = "FluentDocker.Tests.Fixtures.Dmr",
        RelativeRootNamespace = string.Empty
      };

      var exception = Assert.Throws<FluentDockerException>(() =>
      {
        foreach (var stream in new ResourceReader([resource]))
        {
          stream.Stream.CopyTo(Stream.Null);
        }
      });

      Assert.Contains("missing-resource.json", exception.Message);
    }
  }
}
