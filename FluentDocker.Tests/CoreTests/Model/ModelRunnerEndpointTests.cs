using System;
using FluentDocker.Model.Models;
using FluentDocker.Tests.CoreTests.Service;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  /// <summary>
  /// Unit tests for the <see cref="ModelRunnerEndpoint"/> value object: URL / path
  /// building, the engine-in-path toggle and environment-based resolution.
  /// <para>
  /// The environment-resolution tests mutate <c>DOCKER_MODEL_RUNNER_URL</c>; this class
  /// joins the non-parallel <see cref="ModelEnvVarsCollection"/> so it never races other
  /// env-mutating model tests on shared process state.
  /// </para>
  /// </summary>
  [Trait("Category", "Unit")]
  [Collection(ModelEnvVarsCollection.Name)]
  public class ModelRunnerEndpointTests
  {
    [Fact]
    public void HostTcp_Default()
    {
      var ep = ModelRunnerEndpoint.HostTcp();

      Assert.Equal(new Uri("http://localhost:12434"), ep.BaseAddress);
      Assert.Equal("llama.cpp", ep.Engine);
      Assert.True(ep.IncludeEngineInPath);
      Assert.Null(ep.UnixSocketPath);
      Assert.Equal("/engines/llama.cpp", ep.EnginePath);
    }

    [Fact]
    public void HostTcp_CustomPort()
    {
      Assert.Equal(new Uri("http://localhost:9000"), ModelRunnerEndpoint.HostTcp(9000).BaseAddress);
    }

    [Fact]
    public void ContainerInternal_UsesInternalDns()
    {
      var ep = ModelRunnerEndpoint.ContainerInternal();
      Assert.Equal(new Uri("http://model-runner.docker.internal:12434"), ep.BaseAddress);
    }

    [Fact]
    public void Custom_UsesProvidedUri()
    {
      var ep = ModelRunnerEndpoint.Custom(new Uri("https://api.example.com:8443"), "vllm");
      Assert.Equal(new Uri("https://api.example.com:8443"), ep.BaseAddress);
      Assert.Equal("vllm", ep.Engine);
    }

    [Fact]
    public void Custom_AuthorityOnly_AppendsEnginePath()
    {
      // Authority-only Custom keeps the documented authority-only behavior: the engine
      // prefix is appended (no path on the supplied Uri to preserve). This locks the
      // no-regression case.
      var ep = ModelRunnerEndpoint.Custom(new Uri("https://host:9000"));
      Assert.Equal("/engines/llama.cpp", ep.EnginePath);
      Assert.Equal(new Uri("https://host:9000/engines/llama.cpp/v1/chat/completions"),
          ep.ResolveUri("/chat/completions"));
    }

    [Fact]
    public void Custom_RootPath_AppendsEnginePath()
    {
      // A bare "/" path is treated as authority-only (no meaningful path to preserve), so
      // the engine prefix is still appended — UNCHANGED from today.
      var ep = ModelRunnerEndpoint.Custom(new Uri("https://host:9000/"));
      Assert.Equal("/engines/llama.cpp", ep.EnginePath);
      Assert.Equal(new Uri("https://host:9000/engines/llama.cpp/v1/chat/completions"),
          ep.ResolveUri("/chat/completions"));
    }

    [Fact]
    public void Custom_PathBearingUri_PreservesPath_LikeRaw()
    {
      // A path-bearing Custom Uri (e.g. https://host:9000/v1) must preserve that path rather
      // than discarding it and re-appending /engines/.../v1 — it behaves exactly like Raw.
      var custom = ModelRunnerEndpoint.Custom(new Uri("https://host:9000/v1"));
      var raw = ModelRunnerEndpoint.Raw(new Uri("https://host:9000/v1"));

      Assert.Equal(raw.BaseAddress, custom.BaseAddress);
      Assert.Equal(raw.EnginePath, custom.EnginePath);
      Assert.Equal(raw.EngineV1Path("/chat/completions"), custom.EngineV1Path("/chat/completions"));
      Assert.Equal(new Uri("https://host:9000/v1/chat/completions"), custom.ResolveUri("/chat/completions"));
      Assert.Equal(raw.ResolveUri("/chat/completions"), custom.ResolveUri("/chat/completions"));
    }

    [Fact]
    public void UnixSocket_ResolvesEnginePath()
    {
      // Capture the current UnixSocket resolved path so the preview caveat (the Docker
      // Desktop host socket may need a routing prefix that is not yet applied) is backed by
      // a test. UnixSocket builds /engines/{engine}/v1/... over the socket today.
      var ep = ModelRunnerEndpoint.UnixSocket("/tmp/docker.sock");
      Assert.Equal("/engines/llama.cpp", ep.EnginePath);
      Assert.Equal("/engines/llama.cpp/v1/models", ep.EngineV1Path("/models"));
    }

    [Fact]
    public void UnixSocket_SetsSocketPath()
    {
      var ep = ModelRunnerEndpoint.UnixSocket("/tmp/docker.sock");
      Assert.Equal("/tmp/docker.sock", ep.UnixSocketPath);
      Assert.NotNull(ep.BaseAddress);
    }

    [Fact]
    public void UnixSocket_DefaultPath_UsesDockerRunSocket()
    {
      var ep = ModelRunnerEndpoint.UnixSocket();
      Assert.NotNull(ep.UnixSocketPath);
      Assert.Contains("docker.sock", ep.UnixSocketPath);
    }

    [Fact]
    public void EngineV1Path_WithEngine()
    {
      var ep = ModelRunnerEndpoint.HostTcp();
      Assert.Equal("/engines/llama.cpp/v1/chat/completions", ep.EngineV1Path("/chat/completions"));
    }

    [Fact]
    public void EngineV1Path_WithoutEngine_Toggle()
    {
      var ep = ModelRunnerEndpoint.HostTcp().WithEngineInPath(false);

      Assert.False(ep.IncludeEngineInPath);
      Assert.Equal("/engines", ep.EnginePath);
      Assert.Equal("/engines/v1/embeddings", ep.EngineV1Path("/embeddings"));
    }

    [Fact]
    public void ResolveUri_CombinesBaseAndEnginePath()
    {
      var ep = ModelRunnerEndpoint.HostTcp();
      Assert.Equal(new Uri("http://localhost:12434/engines/llama.cpp/v1/chat/completions"),
          ep.ResolveUri("/chat/completions"));
    }

    [Fact]
    public void TryFromEnvironment_ReadsDockerModelRunnerUrl()
    {
      const string var = "DOCKER_MODEL_RUNNER_URL";
      var previous = Environment.GetEnvironmentVariable(var);
      try
      {
        Environment.SetEnvironmentVariable(var, "http://10.0.0.5:12434");
        Assert.True(ModelRunnerEndpoint.TryFromEnvironment(out var ep));
        Assert.Equal(new Uri("http://10.0.0.5:12434"), ep.BaseAddress);
      }
      finally
      {
        Environment.SetEnvironmentVariable(var, previous);
      }
    }

    [Fact]
    public void TryFromEnvironment_PathBearingUrl_PreservesEnginePath()
    {
      const string var = "DOCKER_MODEL_RUNNER_URL";
      var previous = Environment.GetEnvironmentVariable(var);
      try
      {
        // A path-bearing injected URL must be preserved, not discarded — otherwise the
        // engine prefix is re-appended and inference hits the wrong path.
        Environment.SetEnvironmentVariable(var, "http://10.0.0.5:12434/engines/v1");
        Assert.True(ModelRunnerEndpoint.TryFromEnvironment(out var ep));
        Assert.Equal(new Uri("http://10.0.0.5:12434/engines/v1/chat/completions"), ep.ResolveUri("/chat/completions"));
      }
      finally
      {
        Environment.SetEnvironmentVariable(var, previous);
      }
    }

    [Fact]
    public void TryFromEnvironment_Unset_ReturnsFalse()
    {
      const string var = "DOCKER_MODEL_RUNNER_URL";
      var previous = Environment.GetEnvironmentVariable(var);
      try
      {
        Environment.SetEnvironmentVariable(var, null);
        Assert.False(ModelRunnerEndpoint.TryFromEnvironment(out var ep));
        Assert.Null(ep);
      }
      finally
      {
        Environment.SetEnvironmentVariable(var, previous);
      }
    }

    [Fact]
    public void Default_IsHostTcp_WhenEnvUnset()
    {
      const string var = "DOCKER_MODEL_RUNNER_URL";
      var previous = Environment.GetEnvironmentVariable(var);
      try
      {
        Environment.SetEnvironmentVariable(var, null);
        Assert.Equal(new Uri("http://localhost:12434"), ModelRunnerEndpoint.Default().BaseAddress);
      }
      finally
      {
        Environment.SetEnvironmentVariable(var, previous);
      }
    }

    [Fact]
    public void Default_HonorsDockerModelRunnerUrl()
    {
      const string var = "DOCKER_MODEL_RUNNER_URL";
      var previous = Environment.GetEnvironmentVariable(var);
      try
      {
        // The documented resolution order is "env var, then host TCP" — Default()
        // must honor DOCKER_MODEL_RUNNER_URL, not blindly return host TCP.
        Environment.SetEnvironmentVariable(var, "http://10.0.0.9:9999");
        Assert.Equal(new Uri("http://10.0.0.9:9999"), ModelRunnerEndpoint.Default().BaseAddress);
      }
      finally
      {
        Environment.SetEnvironmentVariable(var, previous);
      }
    }

    [Fact]
    public void Equality_IsValueBased()
    {
      Assert.Equal(ModelRunnerEndpoint.HostTcp(), ModelRunnerEndpoint.HostTcp());
      Assert.True(ModelRunnerEndpoint.HostTcp() == ModelRunnerEndpoint.HostTcp());
      Assert.Equal(ModelRunnerEndpoint.HostTcp().GetHashCode(), ModelRunnerEndpoint.HostTcp().GetHashCode());
    }

    [Fact]
    public void Equality_DistinguishesEngineInPathToggle()
    {
      Assert.NotEqual(ModelRunnerEndpoint.HostTcp(), ModelRunnerEndpoint.HostTcp().WithEngineInPath(false));
      Assert.True(ModelRunnerEndpoint.HostTcp() != ModelRunnerEndpoint.HostTcp().WithEngineInPath(false));
    }

    [Fact]
    public void Equality_DistinguishesPortAndEngine()
    {
      Assert.NotEqual(ModelRunnerEndpoint.HostTcp(), ModelRunnerEndpoint.HostTcp(9000));
      Assert.NotEqual(ModelRunnerEndpoint.HostTcp(12434, "llama.cpp"), ModelRunnerEndpoint.HostTcp(12434, "vllm"));
    }
  }
}
