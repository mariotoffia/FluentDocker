using System;
using FluentDocker.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  [Trait("Category", "Unit")]
  public class SharedHttpClientTests
  {
    [Fact]
    public void Instance_IsNotNull()
    {
      Assert.NotNull(SharedHttpClient.Instance);
    }

    [Fact]
    public void Instance_ReturnsSameInstance_AcrossCalls()
    {
      var first = SharedHttpClient.Instance;
      var second = SharedHttpClient.Instance;
      Assert.Same(first, second);
    }

    [Fact]
    public void Instance_HasReasonableTimeout()
    {
      Assert.Equal(TimeSpan.FromSeconds(30), SharedHttpClient.Instance.Timeout);
    }
  }
}
