using System.Reflection;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// ParseServiceInspect tests for DockerCliServiceDriver.
  /// The method is internal, so it is accessed via reflection.
  /// </summary>
  public partial class DockerCliServiceDriverTests
  {
    /// <summary>
    /// Reflection accessor for the internal static ParseServiceInspect method.
    /// </summary>
    private static readonly MethodInfo ParseServiceInspectMethod =
        typeof(DockerCliServiceDriver).GetMethod(
            "ParseServiceInspect",
            BindingFlags.Static | BindingFlags.NonPublic);

    /// <summary>
    /// Invokes the internal ParseServiceInspect via reflection.
    /// </summary>
    private static ServiceDetails InvokeParseServiceInspect(string json)
    {
      Assert.NotNull(ParseServiceInspectMethod);
      return (ServiceDetails)ParseServiceInspectMethod.Invoke(
          null, [json]);
    }

    #region ParseServiceInspect -- Replicated Mode

    [Fact]
    public void ParseServiceInspect_ReplicatedMode_ParsesModeAndReplicas()
    {
      const string json = @"[{
        ""ID"": ""svc123"",
        ""Version"": { ""Index"": 42 },
        ""Spec"": {
          ""Name"": ""web"",
          ""Mode"": {
            ""Replicated"": { ""Replicas"": 3 }
          },
          ""TaskTemplate"": {
            ""ContainerSpec"": {
              ""Image"": ""nginx:latest""
            }
          }
        }
      }]";

      var details = InvokeParseServiceInspect(json);

      Assert.NotNull(details);
      Assert.Equal("svc123", details.Id);
      Assert.Equal(42, details.Version);
      Assert.Equal("web", details.Name);
      Assert.Equal("replicated", details.Mode);
      Assert.Equal(3, details.Replicas);
      Assert.Equal("nginx:latest", details.Image);
    }

    #endregion

    #region ParseServiceInspect -- Global Mode

    [Fact]
    public void ParseServiceInspect_GlobalMode_ParsesModeCorrectly()
    {
      const string json = @"[{
        ""ID"": ""svc456"",
        ""Version"": { ""Index"": 10 },
        ""Spec"": {
          ""Name"": ""agent"",
          ""Mode"": {
            ""Global"": {}
          },
          ""TaskTemplate"": {
            ""ContainerSpec"": {
              ""Image"": ""datadog/agent:latest""
            }
          }
        }
      }]";

      var details = InvokeParseServiceInspect(json);

      Assert.NotNull(details);
      Assert.Equal("global", details.Mode);
      Assert.Equal(0, details.Replicas);
    }

    #endregion

    #region ParseServiceInspect -- Command, Args, Env, Labels

    [Fact]
    public void ParseServiceInspect_CommandAndArgs_ParsedCorrectly()
    {
      const string json = @"[{
        ""ID"": ""cmd1"",
        ""Version"": { ""Index"": 1 },
        ""Spec"": {
          ""Name"": ""runner"",
          ""Mode"": { ""Replicated"": { ""Replicas"": 1 } },
          ""TaskTemplate"": {
            ""ContainerSpec"": {
              ""Image"": ""myapp:v1"",
              ""Command"": [""/bin/sh"", ""-c""],
              ""Args"": [""echo hello"", ""--verbose""]
            }
          }
        }
      }]";

      var details = InvokeParseServiceInspect(json);

      Assert.NotNull(details);
      Assert.Equal(new[] { "/bin/sh", "-c" }, details.Command);
      Assert.Equal(new[] { "echo hello", "--verbose" }, details.Args);
    }

    [Fact]
    public void ParseServiceInspect_Environment_ParsedAsKeyValuePairs()
    {
      const string json = @"[{
        ""ID"": ""env1"",
        ""Version"": { ""Index"": 5 },
        ""Spec"": {
          ""Name"": ""api"",
          ""Mode"": { ""Replicated"": { ""Replicas"": 2 } },
          ""TaskTemplate"": {
            ""ContainerSpec"": {
              ""Image"": ""api:latest"",
              ""Env"": [
                ""DB_HOST=postgres"",
                ""DB_PORT=5432"",
                ""LOG_LEVEL=debug""
              ]
            }
          }
        }
      }]";

      var details = InvokeParseServiceInspect(json);

      Assert.NotNull(details);
      Assert.Equal(3, details.Environment.Count);
      Assert.Equal("postgres", details.Environment["DB_HOST"]);
      Assert.Equal("5432", details.Environment["DB_PORT"]);
      Assert.Equal("debug", details.Environment["LOG_LEVEL"]);
    }

    [Fact]
    public void ParseServiceInspect_Labels_ParsedFromSpec()
    {
      const string json = @"[{
        ""ID"": ""lbl1"",
        ""Version"": { ""Index"": 7 },
        ""Spec"": {
          ""Name"": ""tagged"",
          ""Labels"": {
            ""com.example.env"": ""production"",
            ""com.example.team"": ""platform""
          },
          ""Mode"": { ""Replicated"": { ""Replicas"": 1 } },
          ""TaskTemplate"": {
            ""ContainerSpec"": { ""Image"": ""app:latest"" }
          }
        }
      }]";

      var details = InvokeParseServiceInspect(json);

      Assert.NotNull(details);
      Assert.Equal(2, details.Labels.Count);
      Assert.Equal("production", details.Labels["com.example.env"]);
      Assert.Equal("platform", details.Labels["com.example.team"]);
    }

    #endregion

    #region ParseServiceInspect -- Edge Cases

    [Fact]
    public void ParseServiceInspect_EmptyArray_ReturnsNull()
    {
      var details = InvokeParseServiceInspect("[]");
      Assert.Null(details);
    }

    [Fact]
    public void ParseServiceInspect_NotArray_ReturnsNull()
    {
      var details = InvokeParseServiceInspect(@"{""ID"": ""svc1""}");
      Assert.Null(details);
    }

    [Fact]
    public void ParseServiceInspect_NoCommandOrArgs_ReturnsEmptyArrays()
    {
      const string json = @"[{
        ""ID"": ""noCmd"",
        ""Version"": { ""Index"": 1 },
        ""Spec"": {
          ""Name"": ""minimal"",
          ""Mode"": { ""Replicated"": { ""Replicas"": 1 } },
          ""TaskTemplate"": {
            ""ContainerSpec"": { ""Image"": ""alpine"" }
          }
        }
      }]";

      var details = InvokeParseServiceInspect(json);

      Assert.NotNull(details);
      Assert.Empty(details.Command);
      Assert.Empty(details.Args);
      Assert.Empty(details.Environment);
      Assert.Empty(details.Labels);
    }

    [Fact]
    public void ParseServiceInspect_EnvWithEqualsInValue_ParsedCorrectly()
    {
      const string json = @"[{
        ""ID"": ""eq1"",
        ""Version"": { ""Index"": 1 },
        ""Spec"": {
          ""Name"": ""eqtest"",
          ""Mode"": { ""Replicated"": { ""Replicas"": 1 } },
          ""TaskTemplate"": {
            ""ContainerSpec"": {
              ""Image"": ""app"",
              ""Env"": [""CONN=host=db;port=5432;user=admin""]
            }
          }
        }
      }]";

      var details = InvokeParseServiceInspect(json);

      Assert.NotNull(details);
      Assert.Single(details.Environment);
      Assert.Equal(
          "host=db;port=5432;user=admin",
          details.Environment["CONN"]);
    }

    [Fact]
    public void ParseServiceInspect_LargeVersionIndex_ParsedCorrectly()
    {
      const string json = @"[{
        ""ID"": ""ver1"",
        ""Version"": { ""Index"": 999999999 },
        ""Spec"": {
          ""Name"": ""versioned"",
          ""Mode"": { ""Replicated"": { ""Replicas"": 1 } },
          ""TaskTemplate"": {
            ""ContainerSpec"": { ""Image"": ""app"" }
          }
        }
      }]";

      var details = InvokeParseServiceInspect(json);

      Assert.NotNull(details);
      Assert.Equal(999999999, details.Version);
    }

    [Fact]
    public void ParseServiceInspect_NoMode_ModeIsNull()
    {
      const string json = @"[{
        ""ID"": ""nomode"",
        ""Version"": { ""Index"": 1 },
        ""Spec"": {
          ""Name"": ""nomode-svc"",
          ""TaskTemplate"": {
            ""ContainerSpec"": { ""Image"": ""app"" }
          }
        }
      }]";

      var details = InvokeParseServiceInspect(json);

      Assert.NotNull(details);
      Assert.Null(details.Mode);
    }

    [Fact]
    public void ParseServiceInspect_RawJson_Preserved()
    {
      const string json =
          @"[{""ID"":""raw"",""Version"":{""Index"":1}," +
          @"""Spec"":{""Name"":""r"",""Mode"":{""Replicated"":" +
          @"{""Replicas"":1}},""TaskTemplate"":{""ContainerSpec"":" +
          @"{""Image"":""a""}}}}]";

      var details = InvokeParseServiceInspect(json);

      Assert.NotNull(details);
      Assert.Equal(json, details.RawJson);
    }

    [Fact]
    public void ParseServiceInspect_NoVersion_VersionIsZero()
    {
      const string json = @"[{
        ""ID"": ""nover"",
        ""Spec"": {
          ""Name"": ""nover-svc"",
          ""Mode"": { ""Replicated"": { ""Replicas"": 1 } },
          ""TaskTemplate"": {
            ""ContainerSpec"": { ""Image"": ""app"" }
          }
        }
      }]";

      var details = InvokeParseServiceInspect(json);

      Assert.NotNull(details);
      Assert.Equal(0, details.Version);
    }

    [Fact]
    public void ParseServiceInspect_NoSpec_ReturnsMinimalDetails()
    {
      const string json = @"[{""ID"": ""nospec""}]";

      var details = InvokeParseServiceInspect(json);

      Assert.NotNull(details);
      Assert.Equal("nospec", details.Id);
      Assert.Null(details.Name);
      Assert.Null(details.Image);
    }

    #endregion
  }
}
