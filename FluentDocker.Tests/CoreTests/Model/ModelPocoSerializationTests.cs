using System;
using System.Collections.Generic;
using FluentDocker.Common;
using FluentDocker.Model.Models;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  /// <summary>
  /// Compile + serialization round-trip tests for the DMR management/runtime
  /// POCOs (immutable <c>init</c> records mapped by the CLI/HTTP parsers).
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelPocoSerializationTests
  {
    [Fact]
    public void ModelInfo_RoundTrips()
    {
      var info = new ModelInfo
      {
        Id = "sha256:abc",
        Reference = ModelReference.Parse("ai/smollm2:latest"),
        Tags = new[] { "docker.io/ai/smollm2:latest" },
        Format = "gguf",
        Architecture = "llama",
        ParameterCount = "361.82 M",
        Quantization = "Q4_K_M",
        Size = 268697600,
        Created = new DateTime(2025, 3, 24, 0, 0, 0, DateTimeKind.Utc),
        Config = new Dictionary<string, string> { ["context_size"] = "4096" }
      };

      var json = JsonHelper.Serialize(info);
      var back = JsonHelper.TryDeserialize<ModelInfo>(json);

      Assert.NotNull(back);
      Assert.Equal("sha256:abc", back.Id);
      Assert.Equal("gguf", back.Format);
      Assert.Equal("llama", back.Architecture);
      Assert.Equal(268697600, back.Size);
      Assert.Contains("docker.io/ai/smollm2:latest", back.Tags);
      Assert.Equal("4096", back.Config["context_size"]);
    }

    [Fact]
    public void RunningModel_RoundTrips()
    {
      var rm = new RunningModel
      {
        Reference = ModelReference.Parse("ai/smollm2"),
        Backend = "llama.cpp",
        Mode = "completion",
        MemoryBytes = 1024,
        LastUsed = new DateTime(2026, 6, 23, 0, 0, 0, DateTimeKind.Utc)
      };

      var back = JsonHelper.TryDeserialize<RunningModel>(JsonHelper.Serialize(rm));

      Assert.Equal("llama.cpp", back.Backend);
      Assert.Equal("completion", back.Mode);
      Assert.Equal(1024, back.MemoryBytes);
      Assert.Equal(rm.LastUsed, back.LastUsed);
    }

    [Fact]
    public void ModelRunnerStatus_RoundTrips()
    {
      var status = new ModelRunnerStatus
      {
        Running = true,
        Backend = "llama.cpp",
        Endpoint = new Uri("http://localhost:12434"),
        Error = null
      };

      var back = JsonHelper.TryDeserialize<ModelRunnerStatus>(JsonHelper.Serialize(status));

      Assert.True(back.Running);
      Assert.Equal("llama.cpp", back.Backend);
      Assert.Equal(new Uri("http://localhost:12434"), back.Endpoint);
    }

    [Fact]
    public void ModelRunnerVersion_RoundTrips()
    {
      var v = new ModelRunnerVersion { CliVersion = "v1.2.1", EngineVersion = "b1-ac4cdde", ApiVersion = "v1.2.1" };
      var back = JsonHelper.TryDeserialize<ModelRunnerVersion>(JsonHelper.Serialize(v));

      Assert.Equal("v1.2.1", back.CliVersion);
      Assert.Equal("b1-ac4cdde", back.EngineVersion);
    }

    [Fact]
    public void ModelDiskUsage_RoundTrips()
    {
      var df = new ModelDiskUsage { ModelsSizeBytes = 270_600_000, ModelCount = 2, ReclaimableBytes = 1000 };
      var back = JsonHelper.TryDeserialize<ModelDiskUsage>(JsonHelper.Serialize(df));

      Assert.Equal(270_600_000, back.ModelsSizeBytes);
      Assert.Equal(2, back.ModelCount);
      Assert.Equal(1000, back.ReclaimableBytes);
    }

    [Fact]
    public void ModelPruneResult_RoundTrips()
    {
      var pr = new ModelPruneResult { Removed = new[] { "ai/old:latest" }, ReclaimedBytes = 5000 };
      var back = JsonHelper.TryDeserialize<ModelPruneResult>(JsonHelper.Serialize(pr));

      Assert.Contains("ai/old:latest", back.Removed);
      Assert.Equal(5000, back.ReclaimedBytes);
    }

    [Fact]
    public void ModelPullProgress_FractionComputed()
    {
      var p = new ModelPullProgress { Status = "Downloading", Layer = "abc", Current = 50, Total = 200 };

      Assert.Equal(0.25d, p.Fraction, 5);
    }

    [Fact]
    public void ModelPullProgress_ZeroTotal_FractionIsZero()
    {
      var p = new ModelPullProgress { Status = "Verifying", Current = 0, Total = 0 };

      Assert.Equal(0d, p.Fraction);
    }
  }
}
