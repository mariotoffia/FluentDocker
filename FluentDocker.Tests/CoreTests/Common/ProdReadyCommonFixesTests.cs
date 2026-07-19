using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FluentDocker.Common;
using FluentDocker.Extensions;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  [Trait("Category", "Unit")]
  public sealed class ProdReadyCommonFixesTests
  {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Map_OnFailure_PreservesOutput(bool withContext)
    {
      var response = withContext
          ? CommandResponse<int>.Fail(
              "pull failed",
              ErrorCodes.Image.PullFailed,
              new ErrorContext("Pull"),
              exitCode: 1,
              output: "captured output")
          : CommandResponse<int>.Fail(
              "pull failed",
              ErrorCodes.Image.PullFailed,
              exitCode: 1,
              output: "captured output");

      var result = response.Map(x => x.ToString(CultureInfo.InvariantCulture));

      Assert.False(result.Success);
      Assert.Equal("captured output", result.Output);
    }

    [Fact]
    public void Map_OnSuccessWithNullData_PreservesOutputOnContractFailure()
    {
      var response = CommandResponse<string>.Ok(null!, "captured stdout", 0);

      var result = response.Map(value => value.Length);

      Assert.False(result.Success);
      Assert.Equal("captured stdout", result.Output);
    }

    [Fact]
    public void Map_WhenMapperReturnsNull_PreservesOutputOnContractFailure()
    {
      var response = CommandResponse<string>.Ok("data", "mapper stdout", 0);

      var result = response.Map<string, string?>(_ => null);

      Assert.False(result.Success);
      Assert.Equal("mapper stdout", result.Output);
    }

    [Fact]
    public void ErrorContext_ToString_IncludesMetadataAndTruncatedStdErr()
    {
      var context = ErrorContextExtensions.ForContainer("Start", "abc123");
      context.WithStdErr(new string('x', 600));

      var text = context.ToString();

      Assert.Contains("containerId=abc123", text);
      Assert.Contains("StdErr: ...", text);
    }

    [Theory]
    [InlineData("unauthorized: authentication required")]
    [InlineData("denied: requested access to the resource is denied")]
    [InlineData("manifest unknown")]
    [InlineData("not found")]
    public void ImagePullException_Classifier_MarksRegistryPermanentFailuresNonTransient(string reason)
    {
      Assert.False(ImagePullException.IsTransientReason(reason));
    }

    [Theory]
    [InlineData("network timeout")]
    [InlineData("connection reset by peer")]
    public void ImagePullException_Classifier_MarksNetworkFailuresTransient(string reason)
    {
      Assert.True(ImagePullException.IsTransientReason(reason));
    }

    [Fact]
    public void WrapValue_NameContainingNewline_Throws()
    {
      var ex = Assert.Throws<FluentDockerException>(() =>
          new TemplateString[] { "BAD\nNAME=value" }.WrapValue());

      Assert.Contains("ENV/LABEL names", ex.Message);
    }

    [Fact]
    public void WrapValue_ValueContainingNullCharacter_Throws()
    {
      var ex = Assert.Throws<FluentDockerException>(() =>
          new TemplateString[] { "NAME=bad\0value" }.WrapValue());

      Assert.Contains("ENV/LABEL values", ex.Message);
    }

    [Fact]
    public void OptionIfExists_WhitespaceValue_QuotesArgument()
    {
      var result = new StringBuilder("docker build")
          .OptionIfExists("--build-arg ", "NAME=value with space")
          .ToString();

      Assert.Equal("docker build --build-arg \"NAME=value with space\"", result);
    }

    [Fact]
    public void TemplateString_EnvironmentToken_UsesDirectEnvironmentLookup()
    {
      var key = "FLUENTDOCKER_PRODREADY_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
      var previous = Environment.GetEnvironmentVariable(key);

      try
      {
        Environment.SetEnvironmentVariable(key, "resolved");
        var lookupKey = OperatingSystem.IsWindows() ? key.ToLowerInvariant() : key;

        var rendered = new TemplateString($"prefix_${{E_{lookupKey}}}_suffix").Rendered;

        Assert.Equal("prefix_resolved_suffix", rendered);
      }
      finally
      {
        Environment.SetEnvironmentVariable(key, previous);
      }
    }

    [Fact]
    public void ParseByteValue_OnOverflow_Saturates()
    {
      var result = CliOutputParser.ParseByteValue("9223372036854775808B");

      Assert.Equal(long.MaxValue, result);
    }

    [Fact]
    public void JsonHelper_StringNumberDriftOutsideKnownInspectFields_Fails()
    {
      var ok = JsonHelper.TryDeserialize<StringValueDto>(
          """{"Value":123}""",
          out var value,
          out var error);

      Assert.False(ok);
      Assert.Null(value);
      Assert.IsType<JsonException>(error);
    }

    private sealed class StringValueDto
    {
      public string? Value { get; set; }
    }
  }
}
