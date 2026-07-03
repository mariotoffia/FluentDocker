using System;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Extensions
{
  [Trait("Category", "Unit")]
  public class RemovedDeadExtensionTests
  {
    [Theory]
    [InlineData("FluentDocker.Extensions.OsExtensions, FluentDocker")]
    [InlineData("FluentDocker.Extensions.HttpExtensions, FluentDocker")]
    public void DeadExtensionTypes_AreRemoved(string typeName)
    {
      Assert.Null(Type.GetType(typeName, throwOnError: false));
    }
  }
}
