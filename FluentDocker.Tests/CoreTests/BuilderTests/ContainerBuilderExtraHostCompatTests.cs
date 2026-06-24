using FluentDocker.Builders;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// API-compatibility test for <see cref="IContainerBuilder.WithExtraHost"/>: it is a
  /// <b>default interface method</b>, so adding it to the published interface does not
  /// break existing third-party implementations or mocks (a source/binary-compatible
  /// addition). The default body throws; the built-in builder overrides it.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ContainerBuilderExtraHostCompatTests
  {
    [Fact]
    public void WithExtraHost_IsDefaultInterfaceMethod()
    {
      var method = typeof(IContainerBuilder).GetMethod(nameof(IContainerBuilder.WithExtraHost));

      Assert.NotNull(method);
      // A default interface method is non-abstract: existing implementors compile
      // unchanged because they are not forced to implement the new member.
      Assert.False(method.IsAbstract, "WithExtraHost must be a default interface method (non-breaking addition)");
    }
  }
}
