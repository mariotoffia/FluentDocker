using FluentDocker.Model.Containers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  /// <summary>
  /// Tests for ContainerCreateParams validation and nullable defaults.
  /// Verifies sentinel values (float.MinValue, int.MinValue) are replaced with nullable types.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ContainerCreateParamsValidationTests
  {
    #region Default Values

    [Fact]
    public void Cpus_Default_IsNull()
    {
      var p = new ContainerCreateParams();
      Assert.Null(p.Cpus);
    }

    [Fact]
    public void CpuShares_Default_IsNull()
    {
      var p = new ContainerCreateParams();
      Assert.Null(p.CpuShares);
    }

    #endregion

    #region Valid Values

    [Fact]
    public void Cpus_SetToValidValue_RetainsValue()
    {
      var p = new ContainerCreateParams { Cpus = 1.5f };
      Assert.Equal(1.5f, p.Cpus);
    }

    [Fact]
    public void Cpus_SetToZero_IsValid()
    {
      var p = new ContainerCreateParams { Cpus = 0f };
      Assert.Equal(0f, p.Cpus);
    }

    [Fact]
    public void CpuShares_SetToValidValue_RetainsValue()
    {
      var p = new ContainerCreateParams { CpuShares = 512 };
      Assert.Equal(512, p.CpuShares);
    }

    #endregion

    #region Rendering with nullable values

    [Fact]
    public void ToString_WithNullCpus_OmitsCpuFlag()
    {
      var p = new ContainerCreateParams();
      var result = p.ToString();
      Assert.DoesNotContain("--cpus", result);
    }

    [Fact]
    public void ToString_WithValidCpus_IncludesCpuFlag()
    {
      var p = new ContainerCreateParams { Cpus = 2.0f };
      var result = p.ToString();
      Assert.Contains("--cpus=", result);
    }

    [Fact]
    public void ToString_WithNullCpuShares_OmitsCpuSharesFlag()
    {
      var p = new ContainerCreateParams();
      var result = p.ToString();
      Assert.DoesNotContain("--cpu-shares", result);
    }

    [Fact]
    public void ToString_WithValidCpuShares_IncludesCpuSharesFlag()
    {
      var p = new ContainerCreateParams { CpuShares = 512 };
      var result = p.ToString();
      Assert.Contains("--cpu-shares=", result);
    }

    #endregion
  }
}
