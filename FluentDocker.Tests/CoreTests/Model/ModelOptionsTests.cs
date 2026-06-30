using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  /// <summary>
  /// Default-value and shape tests for the DMR option records and
  /// <see cref="ModelRunnerCapabilities"/>.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelOptionsTests
  {
    [Fact]
    public void ModelRunOptions_Defaults()
    {
      var o = new ModelRunOptions();

      Assert.False(o.Debug);
    }

    [Fact]
    public void ModelConfigureOptions_Defaults()
    {
      var o = new ModelConfigureOptions();

      Assert.Null(o.ContextSize);
      Assert.False(o.ResetContextSize);
      Assert.Null(o.Backend);
      Assert.True(o.IsAutoBackend);
      Assert.Null(o.RuntimeFlags);
      Assert.Null(o.HfOverridesJson);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("auto")]
    [InlineData("AUTO")]
    public void ModelConfigureOptions_AutoBackend_IsAuto(string? backend)
    {
      Assert.True(new ModelConfigureOptions { Backend = backend! }.IsAutoBackend); // intentional null to verify null-handling
    }

    [Fact]
    public void ModelConfigureOptions_Settable()
    {
      var o = new ModelConfigureOptions
      {
        ContextSize = 8192,
        ResetContextSize = true,
        Backend = "vllm",
        RuntimeFlags = new[] { "--temp", "0.7" },
        HfOverridesJson = "{\"max_model_len\":8192}"
      };

      Assert.Equal(8192, o.ContextSize);
      Assert.True(o.ResetContextSize);
      Assert.Equal("vllm", o.Backend);
      Assert.False(o.IsAutoBackend);
      Assert.Equal(2, o.RuntimeFlags.Count);
      Assert.Equal("{\"max_model_len\":8192}", o.HfOverridesJson);
    }

    [Fact]
    public void ModelPackageRequest_Settable()
    {
      var r = new ModelPackageRequest
      {
        GgufPath = "/tmp/m.gguf",
        Target = ModelReference.Parse("ai/mine:1"),
        Push = true,
        License = "/tmp/LICENSE.txt"
      };

      Assert.Equal("/tmp/m.gguf", r.GgufPath);
      Assert.Equal("ai/mine:1", r.Target.ToString());
      Assert.True(r.Push);
      Assert.Equal("/tmp/LICENSE.txt", r.License);
    }

    [Fact]
    public void ModelRunnerInstallOptions_Defaults()
    {
      Assert.Null(new ModelRunnerInstallOptions().Gpu);
    }

    [Fact]
    public void ModelRunnerUninstallOptions_Defaults()
    {
      var o = new ModelRunnerUninstallOptions();
      Assert.False(o.RemoveImages);
      Assert.False(o.RemoveModels);
    }

    [Fact]
    public void ModelRunnerCapabilities_Settable()
    {
      var c = new ModelRunnerCapabilities
      {
        SupportsManagement = true,
        SupportsRuntimeControl = true,
        SupportsInference = true,
        SupportsStreaming = true,
        SupportsEmbeddings = true,
        SupportsPackaging = true,
        DefaultBackend = "llama.cpp",
        AvailableBackends = new[] { "llama.cpp", "vllm" }
      };

      Assert.True(c.SupportsManagement);
      Assert.True(c.SupportsInference);
      Assert.Equal("llama.cpp", c.DefaultBackend);
      Assert.Contains("vllm", c.AvailableBackends);
    }

    [Fact]
    public void ModelRunnerCapabilities_Defaults_AllFalse()
    {
      var c = new ModelRunnerCapabilities();
      Assert.False(c.SupportsManagement);
      Assert.False(c.SupportsInference);
      Assert.Null(c.DefaultBackend);
    }
  }
}
