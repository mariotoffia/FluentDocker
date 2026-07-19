using System.Globalization;
using System.Threading;
using FluentDocker.Model.Containers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  [Trait("Category", "Unit")]
  public class ContainerBuildParamsTests
  {
    [Fact]
    public void ToString_RendersFloatOptionsWithInvariantCulture()
    {
      var previousCulture = Thread.CurrentThread.CurrentCulture;
      var previousUiCulture = Thread.CurrentThread.CurrentUICulture;
      try
      {
        var swedish = CultureInfo.GetCultureInfo("sv-SE");
        Thread.CurrentThread.CurrentCulture = swedish;
        Thread.CurrentThread.CurrentUICulture = swedish;

#pragma warning disable CS0618
        var args = new ContainerBuildParams
        {
          CpuShares = 1.5f,
          CpuPeriod = 2.5f,
          CpuQuota = 3.5f
        }.ToString();
#pragma warning restore CS0618

        Assert.Contains("--cpu-shares 1.5", args);
        Assert.Contains("--cpu-period 2.5", args);
        Assert.Contains("--cpu-quota 3.5", args);
        Assert.DoesNotContain("1,5", args);
      }
      finally
      {
        Thread.CurrentThread.CurrentCulture = previousCulture;
        Thread.CurrentThread.CurrentUICulture = previousUiCulture;
      }
    }
  }
}
