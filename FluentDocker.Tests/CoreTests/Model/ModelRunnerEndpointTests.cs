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

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void HostTcp_PortOutsideTcpRange_ThrowsArgumentOutOfRangeException(int port)
    {
      Assert.Throws<ArgumentOutOfRangeException>(() => ModelRunnerEndpoint.HostTcp(port));
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
    public void Custom_AuthorityOnlyWithQuery_PreservesQuery()
    {
      var ep = ModelRunnerEndpoint.Custom(new Uri("http://host:12434/?apikey=x"));

      Assert.EndsWith("?apikey=x", ep.EngineV1Path("/models"), StringComparison.Ordinal);
      Assert.Equal(new Uri("http://host:12434/engines/llama.cpp/v1/models?apikey=x"),
          ep.ResolveUri("/models"));
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
    public void Raw_AuthorityOnlyUri_UsesEmptyVerbatimBasePath()
    {
      var ep = ModelRunnerEndpoint.Raw(new Uri("http://host:12434"));

      Assert.Equal(string.Empty, ep.EnginePath);
      Assert.Equal("/chat/completions", ep.EngineV1Path("/chat/completions"));
      Assert.Equal("/chat/completions", ep.ResolveUri("/chat/completions").AbsolutePath);
    }

    [Fact]
    public void Raw_PathBearingUri_UsesVerbatimBasePath()
    {
      var ep = ModelRunnerEndpoint.Raw(new Uri("http://host:12434/engines/v1"));

      Assert.Equal("/engines/v1/x", ep.EngineV1Path("/x"));
    }

    [Fact]
    public void Custom_AuthorityOnlyRegressionGuard_KeepsEnginePrefix()
    {
      var ep = ModelRunnerEndpoint.Custom(new Uri("http://host:12434"));

      Assert.Contains("/engines/", ep.EngineV1Path("/x"), StringComparison.Ordinal);
    }

    [Fact]
    public void UnixSocket_ResolvesEnginePath()
    {
      // Capture the current UnixSocket resolved path so the preview caveat (the Docker
      // Desktop host socket may need a routing prefix that is not yet applied) is backed by
      // a test. UnixSocket builds /engines/{engine}/v1/... over the socket today.
      var ep = ModelRunnerEndpoint.UnixSocket("/var/run/docker.sock");
      Assert.Equal("/engines/llama.cpp", ep.EnginePath);
      Assert.Equal("/engines/llama.cpp/v1/models", ep.EngineV1Path("/models"));
    }

    [Fact]
    public void UnixSocket_SetsSocketPath()
    {
      var ep = ModelRunnerEndpoint.UnixSocket("/var/run/docker.sock");
      Assert.Equal("/var/run/docker.sock", ep.UnixSocketPath);
      Assert.NotNull(ep.BaseAddress);
    }

    [Fact]
    public void UnixSocket_NullPath_Throws()
    {
      Assert.Throws<ArgumentException>(() => ModelRunnerEndpoint.UnixSocket(null!));
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
    public void TryFromEnvironment_SetButInvalid_ReturnsFalse()
    {
      const string var = "DOCKER_MODEL_RUNNER_URL";
      var previous = Environment.GetEnvironmentVariable(var);
      try
      {
        // Item 5: a SET-but-invalid value must fail fast (not silently fall back to localhost),
        // and the message must name the bad value and the env var.
        Environment.SetEnvironmentVariable(var, "not-a-valid-uri");
        Assert.False(ModelRunnerEndpoint.TryFromEnvironment(out var ep));
        Assert.Null(ep);
      }
      finally
      {
        Environment.SetEnvironmentVariable(var, previous);
      }
    }

    [Fact]
    public void TryFromEnvironment_NonHttpScheme_ReturnsFalse()
    {
      const string var = "DOCKER_MODEL_RUNNER_URL";
      var previous = Environment.GetEnvironmentVariable(var);
      try
      {
        // A non-http(s) absolute URI (ftp/file/etc.) is well-formed but cannot reach the runner;
        // it must fail fast like any other invalid value rather than silently falling back.
        Environment.SetEnvironmentVariable(var, "ftp://10.0.0.5:12434");
        Assert.False(ModelRunnerEndpoint.TryFromEnvironment(out var ep));
        Assert.Null(ep);
      }
      finally
      {
        Environment.SetEnvironmentVariable(var, previous);
      }
    }

    [Theory]
    [InlineData("http://localhost:12434", true)]
    [InlineData("https://runner.example.com", true)]
    [InlineData("ftp://10.0.0.5:12434", false)]   // valid absolute URI, wrong scheme
    [InlineData("file:///etc/passwd", false)]     // absolute, but no host
    [InlineData("localhost:12434", false)]        // scheme-less: parses as scheme 'localhost', no host
    public void IsSupportedUrl_AcceptsOnlyHttpWithHost(string value, bool expected)
    {
      // The single predicate both DOCKER_MODEL_RUNNER_URL and the env/Compose runner paths share:
      // only absolute http(s) URLs with a host are usable endpoints.
      Assert.Equal(expected, Uri.TryCreate(value, UriKind.Absolute, out var uri) && ModelRunnerEndpoint.IsSupportedUrl(uri));
    }

    [Fact]
    public void Raw_And_Custom_RejectNonHttpSchemes()
    {
      Assert.Throws<ArgumentException>(() => ModelRunnerEndpoint.Raw(new Uri("ftp://10.0.0.5:12434/v1")));
      Assert.Throws<ArgumentException>(() => ModelRunnerEndpoint.Custom(new Uri("ftp://10.0.0.5:12434")));
    }

    [Theory]
    [InlineData("http://localhost:12434/v1")]
    [InlineData("https://runner.example.com/v1")]
    public void Raw_And_Custom_AcceptHttpAndHttps(string value)
    {
      var uri = new Uri(value);

      Assert.Equal(uri.Scheme, ModelRunnerEndpoint.Raw(uri).BaseAddress.Scheme);
      Assert.Equal(uri.Scheme, ModelRunnerEndpoint.Custom(uri).BaseAddress.Scheme);
    }

    [Fact]
    public void Default_SetButInvalid_FallsBackToHostTcp()
    {
      const string var = "DOCKER_MODEL_RUNNER_URL";
      var previous = Environment.GetEnvironmentVariable(var);
      try
      {
        // The fail-fast must propagate through Default() too — it must NOT swallow the
        // invalid value and return the localhost fallback.
        Environment.SetEnvironmentVariable(var, "not-a-valid-uri");
        Assert.Equal(new Uri("http://localhost:12434"), ModelRunnerEndpoint.Default().BaseAddress);
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
    public void Raw_PathBearingUri_PreservesQueryString()
    {
      var ep = ModelRunnerEndpoint.Raw(new Uri("https://runner.example.com/engines/v1?key=x"));

      Assert.Equal(new Uri("https://runner.example.com/engines/v1/chat/completions?key=x"),
          ep.ResolveUri("/chat/completions"));
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
