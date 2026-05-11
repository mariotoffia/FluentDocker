using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using FluentDocker.Common;

namespace FluentDocker.Benchmarks
{
  /// <summary>
  /// Phase D benchmark: JsonDocument.Parse+Clone vs JsonNode.Parse
  /// for the navigate-and-discard pattern used in Docker/Podman CLI drivers.
  ///
  /// Tests three representative payload sizes:
  ///   Small  (~200B)  — container list item
  ///   Medium (~2KB)   — container inspect
  ///   Large  (~10KB)  — detailed inspect with mounts/networks
  /// </summary>
  [MemoryDiagnoser]
  [InProcess]
  [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
  [CategoriesColumn]
  public class JsonParsingBenchmarks
  {
    #region Test Data

    private const string SmallJson = """
      {
        "Id": "abc123def456",
        "Names": ["/nginx-test"],
        "Image": "nginx:latest",
        "State": "running",
        "Status": "Up 3 hours",
        "Created": 1719500000
      }
      """;

    private const string MediumJson = """
      {
        "Id": "abc123def4567890abcdef1234567890abcdef1234567890abcdef12345678",
        "Created": "2024-06-15T10:30:00.123456789Z",
        "Path": "/docker-entrypoint.sh",
        "Args": ["nginx", "-g", "daemon off;"],
        "State": {
          "Status": "running",
          "Running": true,
          "Paused": false,
          "Restarting": false,
          "OOMKilled": false,
          "Dead": false,
          "Pid": 12345,
          "ExitCode": 0,
          "Error": "",
          "StartedAt": "2024-06-15T10:30:01.234567Z",
          "FinishedAt": "0001-01-01T00:00:00Z"
        },
        "Image": "sha256:abcdef1234567890abcdef1234567890abcdef1234567890abcdef12345678",
        "Name": "/nginx-test",
        "RestartCount": 0,
        "Driver": "overlay2",
        "Platform": "linux",
        "Config": {
          "Hostname": "abc123def456",
          "Domainname": "",
          "User": "",
          "Env": ["PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin", "NGINX_VERSION=1.25.1"],
          "Cmd": ["nginx", "-g", "daemon off;"],
          "Image": "nginx:latest",
          "WorkingDir": "",
          "Entrypoint": ["/docker-entrypoint.sh"],
          "Labels": {
            "maintainer": "NGINX Docker Maintainers",
            "com.docker.compose.project": "web",
            "com.docker.compose.service": "nginx"
          }
        },
        "NetworkSettings": {
          "Bridge": "",
          "Gateway": "172.17.0.1",
          "IPAddress": "172.17.0.2",
          "IPPrefixLen": 16,
          "MacAddress": "02:42:ac:11:00:02",
          "Networks": {
            "bridge": {
              "NetworkID": "net123",
              "EndpointID": "ep123",
              "Gateway": "172.17.0.1",
              "IPAddress": "172.17.0.2",
              "IPPrefixLen": 16,
              "MacAddress": "02:42:ac:11:00:02"
            }
          }
        }
      }
      """;

    private static readonly string LargeJson = BuildLargeJson();

    private static string BuildLargeJson()
    {
      var mounts = string.Join(",\n", Enumerable.Range(0, 20).Select(i => $$"""
        {
          "Type": "bind",
          "Source": "/host/path/{{i}}",
          "Destination": "/container/path/{{i}}",
          "Mode": "rw",
          "RW": true,
          "Propagation": "rprivate"
        }
        """));

      var envVars = string.Join(",", Enumerable.Range(0, 50)
          .Select(i => $"\"VAR_{i}=value_{i}_with_some_longer_content\""));

      return $$"""
        {
          "Id": "abc123def4567890abcdef1234567890abcdef1234567890abcdef12345678",
          "Created": "2024-06-15T10:30:00.123456789Z",
          "Name": "/large-service",
          "State": { "Status": "running", "Running": true, "Pid": 99999 },
          "Config": {
            "Hostname": "large-service",
            "Env": [{{envVars}}],
            "Cmd": ["/bin/sh", "-c", "echo hello"],
            "Labels": { "app": "benchmark", "version": "1.0.0" }
          },
          "Mounts": [{{mounts}}],
          "NetworkSettings": {
            "Networks": {
              "bridge": { "IPAddress": "172.17.0.2", "Gateway": "172.17.0.1" },
              "custom": { "IPAddress": "10.0.0.5", "Gateway": "10.0.0.1" }
            }
          }
        }
        """;
    }

    #endregion

    #region Parse Only — JsonDocument+Clone vs JsonNode

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Small", "Parse")]
    public JsonElement ParseElement_Small() => JsonHelper.ParseElement(SmallJson);

    [Benchmark]
    [BenchmarkCategory("Small", "Parse")]
    public JsonNode ParseNode_Small() => JsonNode.Parse(SmallJson)!;

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Medium", "Parse")]
    public JsonElement ParseElement_Medium() => JsonHelper.ParseElement(MediumJson);

    [Benchmark]
    [BenchmarkCategory("Medium", "Parse")]
    public JsonNode ParseNode_Medium() => JsonNode.Parse(MediumJson)!;

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Large", "Parse")]
    public JsonElement ParseElement_Large() => JsonHelper.ParseElement(LargeJson);

    [Benchmark]
    [BenchmarkCategory("Large", "Parse")]
    public JsonNode ParseNode_Large() => JsonNode.Parse(LargeJson)!;

    #endregion

    #region Parse + Navigate — the real-world pattern

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Small", "Navigate")]
    public string Navigate_Element_Small()
    {
      var el = JsonHelper.ParseElement(SmallJson);
      return el.GetStringOrDefault("Id")
          ?? el.GetStringOrDefault("State");
    }

    [Benchmark]
    [BenchmarkCategory("Small", "Navigate")]
    public string Navigate_Node_Small()
    {
      var node = JsonNode.Parse(SmallJson)!;
      return node["Id"]?.GetValue<string>()
          ?? node["State"]?.GetValue<string>();
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Medium", "Navigate")]
    public (string id, string status, string ip) Navigate_Element_Medium()
    {
      var el = JsonHelper.ParseElement(MediumJson);
      var id = el.GetStringOrDefault("Id");
      var state = el.Prop("State");
      var status = state.HasValue ? state.Value.GetStringOrDefault("Status") : null;
      var nets = el.Prop("NetworkSettings", "Networks", "bridge");
      var ip = nets.HasValue ? nets.Value.GetStringOrDefault("IPAddress") : null;
      return (id, status, ip);
    }

    [Benchmark]
    [BenchmarkCategory("Medium", "Navigate")]
    public (string id, string status, string ip) Navigate_Node_Medium()
    {
      var node = JsonNode.Parse(MediumJson)!;
      var id = node["Id"]?.GetValue<string>();
      var status = node["State"]?["Status"]?.GetValue<string>();
      var ip = node["NetworkSettings"]?["Networks"]?["bridge"]?["IPAddress"]?.GetValue<string>();
      return (id, status, ip);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Large", "Navigate")]
    public (string name, int mountCount, string ip) Navigate_Element_Large()
    {
      var el = JsonHelper.ParseElement(LargeJson);
      var name = el.GetStringOrDefault("Name");
      var mounts = el.Prop("Mounts");
      var mountCount = 0;
      if (mounts.HasValue && mounts.Value.ValueKind == JsonValueKind.Array)
        foreach (var _ in mounts.Value.EnumerateArray())
          mountCount++;
      var nets = el.Prop("NetworkSettings");
      var bridge = nets.HasValue ? nets.Value.Prop("Networks") : null;
      var bridgeNet = bridge.HasValue ? bridge.Value.Prop("bridge") : null;
      var ip = bridgeNet.HasValue ? bridgeNet.Value.GetStringOrDefault("IPAddress") : null;
      return (name, mountCount, ip);
    }

    [Benchmark]
    [BenchmarkCategory("Large", "Navigate")]
    public (string name, int mountCount, string ip) Navigate_Node_Large()
    {
      var node = JsonNode.Parse(LargeJson)!;
      var name = node["Name"]?.GetValue<string>();
      var mountCount = node["Mounts"]?.AsArray().Count ?? 0;
      var ip = node["NetworkSettings"]?["Networks"]?["bridge"]?["IPAddress"]?.GetValue<string>();
      return (name, mountCount, ip);
    }

    #endregion
  }
}
