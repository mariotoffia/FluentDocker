using System;
using System.Reflection;
using FluentDocker.Testing.Core;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class ProcessExitReaperTests
  {
    [Fact]
    public void IsEnabled_UsesOptInEnvironmentVariable()
    {
      var original = Environment.GetEnvironmentVariable(SessionLabel.ReaperEnvironmentVariable);
      try
      {
        Environment.SetEnvironmentVariable(SessionLabel.ReaperEnvironmentVariable, null);
        Assert.False(IsEnabled());

        Environment.SetEnvironmentVariable(SessionLabel.ReaperEnvironmentVariable, "true");
        Assert.True(IsEnabled());
      }
      finally
      {
        Environment.SetEnvironmentVariable(SessionLabel.ReaperEnvironmentVariable, original);
      }
    }

    private static bool IsEnabled()
    {
      var type = typeof(SessionLabel).Assembly.GetType("FluentDocker.Testing.Core.ProcessExitReaper", true)!;
      var method = type.GetMethod("IsEnabled", BindingFlags.Static | BindingFlags.NonPublic)!;
      return (bool)method.Invoke(null, null)!;
    }
  }
}
