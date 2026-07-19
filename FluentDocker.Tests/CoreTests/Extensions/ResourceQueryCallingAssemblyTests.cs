using System.Linq;
using FluentDocker.Resources;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Extensions
{
  [Trait("Category", "Unit")]
  public class ResourceQueryCallingAssemblyTests
  {
    [Fact]
    public void Query_WithoutExplicitAssembly_FindsCallingAssemblyResourcesThroughLinq()
    {
      var results = new ResourceQuery()
        .Namespace("FluentDocker.Tests.Fixtures.Dmr", recursive: true)
        .Query()
        .Where(r => !string.IsNullOrEmpty(r.Resource))
        .ToList();

      Assert.NotEmpty(results);
    }

    [Fact]
    public void Include_WithoutExplicitAssembly_FindsSpecificCallingAssemblyResource()
    {
      var results = new ResourceQuery()
        .Namespace("FluentDocker.Tests.Fixtures.Dmr", recursive: true)
        .Include("chat.json")
        .ToList();

      var result = Assert.Single(results);
      Assert.Equal("chat.json", result.Resource);
    }

    [Fact]
    public void Include_MatchesTrailingSuffixOfFullyQualifiedName()
    {
      // MDL-MAJ-2: ExtractFile lossily reduces "Foo.Bar.child.txt" to "child.txt"; a request with a
      // longer trailing suffix must still find it instead of silently returning nothing.
      var results = new ResourceQuery()
        .Namespace("Foo", recursive: true)
        .Include("Bar.child.txt")
        .ToList();

      Assert.Single(results);
    }
  }
}
