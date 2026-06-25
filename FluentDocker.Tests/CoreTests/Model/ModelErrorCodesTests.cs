using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  /// <summary>
  /// Unit tests for the DMR error-code groups (<see cref="ErrorCodes.Model"/>,
  /// <see cref="ErrorCodes.ModelInference"/>) and <see cref="ModelRunnerException"/>.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelErrorCodesTests
  {
    private static IReadOnlyList<string> CodesOf(Type group) =>
        group.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null))
            .ToList();

    [Fact]
    public void ModelGroup_CodesAreUniqueAndPrefixed()
    {
      var codes = CodesOf(typeof(ErrorCodes.Model));

      Assert.NotEmpty(codes);
      Assert.All(codes, c => Assert.StartsWith("MDL_", c, StringComparison.Ordinal));
      Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void ModelInferenceGroup_CodesAreUniqueAndPrefixed()
    {
      var codes = CodesOf(typeof(ErrorCodes.ModelInference));

      Assert.NotEmpty(codes);
      Assert.All(codes, c => Assert.StartsWith("MIN_", c, StringComparison.Ordinal));
      Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void AllDmrCodes_DoNotCollideWithEachOther()
    {
      var all = CodesOf(typeof(ErrorCodes.Model)).Concat(CodesOf(typeof(ErrorCodes.ModelInference))).ToList();
      Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void KnownCodes_HaveExpectedValues()
    {
      Assert.Equal("MDL_001", ErrorCodes.Model.NotFound);
      Assert.Equal("MDL_002", ErrorCodes.Model.PullFailed);
      Assert.Equal("MIN_001", ErrorCodes.ModelInference.RequestFailed);
      Assert.Equal("MIN_002", ErrorCodes.ModelInference.StreamParseError);
      Assert.Equal("MIN_401", ErrorCodes.ModelInference.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void UninstallFailed_HasDistinctCode_NotSameAsInstallFailed()
    {
      // D20: uninstall failures must be reported with their own distinct error code, not
      // the install-failed code — so callers can distinguish install from uninstall failures.
      Assert.NotEqual(ErrorCodes.Model.InstallFailed, ErrorCodes.Model.UninstallFailed);
      Assert.Equal("MDL_023", ErrorCodes.Model.UninstallFailed);
    }

    [Fact]
    public void ModelRunnerException_IsDriverException()
    {
      var ex = new ModelRunnerException("boom");
      Assert.IsAssignableFrom<DriverException>(ex);
    }

    [Fact]
    public void ModelRunnerException_DefaultErrorCode_IsUnknown()
    {
      var ex = new ModelRunnerException("boom");
      Assert.Equal(ErrorCodes.General.Unknown, ex.ErrorCode);
    }

    [Fact]
    public void ModelRunnerException_CapturesErrorCodeAndInner()
    {
      var inner = new InvalidOperationException("inner");
      var ex = new ModelRunnerException("boom", ErrorCodes.Model.PullFailed, inner);

      Assert.Equal("boom", ex.Message);
      Assert.Equal(ErrorCodes.Model.PullFailed, ex.ErrorCode);
      Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void ModelRunnerException_CapturesContext()
    {
      var ctx = new ErrorContext("Pull") { DriverId = "docker", ExitCode = 1, StdErr = "no such model" };
      var ex = new ModelRunnerException("boom", ErrorCodes.Model.NotFound, ctx);

      Assert.Same(ctx, ex.Context);
      Assert.Equal(ErrorCodes.Model.NotFound, ex.ErrorCode);
    }
  }
}
