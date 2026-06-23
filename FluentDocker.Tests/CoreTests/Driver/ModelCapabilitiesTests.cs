using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Unit tests for the additive model capability flags on
  /// <see cref="DriverCapabilities"/> (must default to <c>false</c> so existing
  /// packs remain source-compatible).
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelCapabilitiesTests
  {
    [Fact]
    public void NewModelFlags_DefaultToFalse()
    {
      var caps = new DriverCapabilities();

      Assert.False(caps.SupportsModels);
      Assert.False(caps.SupportsModelInference);
    }

    [Fact]
    public void Default_DoesNotEnableModelFlags()
    {
      var caps = DriverCapabilities.Default();

      Assert.False(caps.SupportsModels);
      Assert.False(caps.SupportsModelInference);
      // existing flags unaffected
      Assert.True(caps.SupportsContainers);
      Assert.True(caps.SupportsVolumes);
    }

    [Fact]
    public void ModelFlags_AreSettable()
    {
      var caps = new DriverCapabilities { SupportsModels = true, SupportsModelInference = true };

      Assert.True(caps.SupportsModels);
      Assert.True(caps.SupportsModelInference);
    }
  }
}
