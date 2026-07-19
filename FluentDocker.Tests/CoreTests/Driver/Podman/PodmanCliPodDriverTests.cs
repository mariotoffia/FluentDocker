using System;
using System.Collections.Generic;
using System.Reflection;
using FluentDocker.Drivers.Podman;
using FluentDocker.Drivers.Podman.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  [Trait("Category", "Unit")]
  public class PodmanCliPodDriverTests
  {
    #region ParsePodList Tests

    [Fact]
    public void ParsePodList_JsonArray_ReturnsPods()
    {
      // Captured with podman version 6.0.0 on 2026-07-03:
      // podman pod create --name fd-wp5-20260703173403-pod
      // podman run -d --name fd-wp5-20260703173403-ctr --pod fd-wp5-20260703173403-pod docker.io/library/alpine:3.20 sleep 300
      // podman pod ps --format json
      var json = @"[
                {
                    ""Id"": ""8a271f4b082c7800811db676a84dd0c106d452804b4c9cffce4a877eff8348e1"",
                    ""Name"": ""fd-wp5-20260703173403-pod"",
                    ""Status"": ""Running"",
                    ""Created"": ""2026-07-03T17:34:03.364769201+02:00"",
                    ""InfraId"": ""d9017a5a328eb7fac74e73b53bbdf5fcbdefb6ccf06fdf32f61e2760dc309c01"",
                    ""Containers"": [
                        {
                          ""Id"": ""d9017a5a328eb7fac74e73b53bbdf5fcbdefb6ccf06fdf32f61e2760dc309c01"",
                          ""Names"": ""8a271f4b082c-infra"",
                          ""Status"": ""running"",
                          ""RestartCount"": 0
                        },
                        {
                          ""Id"": ""7e29c5d7ade36007d4e7bf802ead186cc59510c4ad17e03a31f84c104feb0154"",
                          ""Names"": ""fd-wp5-20260703173403-ctr"",
                          ""Status"": ""running"",
                          ""RestartCount"": 0
                        }
                    ]
                }
            ]";

      var result = InvokeParsePodList(json);

      Assert.Single(result);
      Assert.Equal("8a271f4b082c7800811db676a84dd0c106d452804b4c9cffce4a877eff8348e1", result[0].Id);
      Assert.Equal("fd-wp5-20260703173403-pod", result[0].Name);
      Assert.Equal("Running", result[0].Status);
      Assert.Equal("d9017a5a328eb7fac74e73b53bbdf5fcbdefb6ccf06fdf32f61e2760dc309c01", result[0].InfraId);
      Assert.Equal(2, result[0].NumContainers);
      Assert.Equal(2, result[0].Containers.Count);
      Assert.Equal("8a271f4b082c-infra", result[0].Containers[0].Name);
      Assert.Equal("running", result[0].Containers[0].State);
      Assert.Equal("fd-wp5-20260703173403-ctr", result[0].Containers[1].Name);
      Assert.Equal("running", result[0].Containers[1].State);
    }

    [Fact]
    public void ParsePodList_NewlineDelimited_ReturnsPods()
    {
      var json = "{\"Id\":\"aaa\",\"Name\":\"pod1\",\"Status\":\"Running\"}\n" +
                 "{\"Id\":\"bbb\",\"Name\":\"pod2\",\"Status\":\"Stopped\"}";

      var result = InvokeParsePodList(json);

      Assert.Equal(2, result.Count);
      Assert.Equal("aaa", result[0].Id);
      Assert.Equal("pod1", result[0].Name);
      Assert.Equal("bbb", result[1].Id);
    }

    [Fact]
    public void ParsePodList_AlternateKeys_ReturnsPods()
    {
      var json = @"[{""id"":""x1"",""name"":""p1"",""status"":""Created"",""num_containers"":2}]";

      var result = InvokeParsePodList(json);

      Assert.Single(result);
      Assert.Equal("x1", result[0].Id);
      Assert.Equal("p1", result[0].Name);
      Assert.Equal("Created", result[0].Status);
      Assert.Equal(2, result[0].NumContainers);
    }

    [Fact]
    public void ParsePodList_EmptyString_ReturnsEmpty()
    {
      Assert.Empty(InvokeParsePodList(""));
      Assert.Empty(InvokeParsePodList(null!));
      Assert.Empty(InvokeParsePodList("   "));
    }

    [Fact]
    public void ParsePodList_EmptyArray_ReturnsEmpty()
    {
      Assert.Empty(InvokeParsePodList("[]"));
    }

    #endregion

    #region ParsePodInspect Tests

    [Fact]
    public void ParsePodInspect_ValidJson_ReturnsDetails()
    {
      var json = @"{
                ""Id"": ""abc123def456"",
                ""Name"": ""my-pod"",
                ""State"": ""Running"",
                ""Created"": ""2026-01-15T10:30:00Z"",
                ""Hostname"": ""my-pod-host"",
                ""InfraContainerID"": ""infra789"",
                ""NumContainers"": 3
            }";

      var result = InvokeParsePodInspect(json);

      Assert.Equal("abc123def456", result.Id);
      Assert.Equal("my-pod", result.Name);
      Assert.Equal("Running", result.State);
      Assert.NotNull(result.Created);
      Assert.Contains("2026", result.Created);
      Assert.Equal("my-pod-host", result.Hostname);
      Assert.Equal("infra789", result.InfraContainerId);
      Assert.Equal(3, result.NumContainers);
    }

    [Fact]
    public void ParsePodInspect_WithContainers_ParsesContainerList()
    {
      var json = @"{
                ""Id"": ""pod1"",
                ""Name"": ""test"",
                ""State"": ""Running"",
                ""Containers"": [
                    { ""Id"": ""c1"", ""Name"": ""web"", ""State"": ""running"" },
                    { ""Id"": ""c2"", ""Name"": ""sidecar"", ""State"": ""exited"" }
                ]
            }";

      var result = InvokeParsePodInspect(json);

      Assert.Equal(2, result.Containers.Count);
      Assert.Equal("c1", result.Containers[0].Id);
      Assert.Equal("web", result.Containers[0].Name);
      Assert.Equal("running", result.Containers[0].State);
      Assert.Equal("c2", result.Containers[1].Id);
      Assert.Equal("sidecar", result.Containers[1].Name);
      Assert.Equal("exited", result.Containers[1].State);
    }

    [Fact]
    public void ParsePodInspect_AlternateKeys_Works()
    {
      var json = @"{
                ""id"": ""p1"",
                ""name"": ""test"",
                ""state"": ""Stopped"",
                ""hostname"": ""h1"",
                ""infraContainerId"": ""inf1"",
                ""num_containers"": 5
            }";

      var result = InvokeParsePodInspect(json);

      Assert.Equal("p1", result.Id);
      Assert.Equal("test", result.Name);
      Assert.Equal("Stopped", result.State);
      Assert.Equal("h1", result.Hostname);
      Assert.Equal("inf1", result.InfraContainerId);
      Assert.Equal(5, result.NumContainers);
    }

    [Fact]
    public void ParsePodInspect_EmptyString_ReturnsEmptyResult()
    {
      var result = InvokeParsePodInspect("");
      Assert.Null(result.Id);
      Assert.Null(result.Name);
    }

    #endregion

    #region BuildCreateArgs Tests

    [Fact]
    public void BuildCreateArgs_MinimalConfig_ReturnsBasicCommand()
    {
      var config = new PodCreateConfig { Name = "my-pod" };
      var result = InvokeBuildCreateArgs(config);
      Assert.Equal("pod create --name my-pod", result);
    }

    [Fact]
    public void BuildCreateArgs_FullConfig_IncludesAllFlags()
    {
      var config = new PodCreateConfig
      {
        Name = "full-pod",
        Hostname = "my-host",
        Network = "my-net",
        InfraImage = "k8s.gcr.io/pause:3.5",
        Share = "ipc,net,uts",
        Labels = new Dictionary<string, string> { { "env", "test" } },
        Dns = ["8.8.8.8"],
        Ports = ["8080:80"]
      };

      var result = InvokeBuildCreateArgs(config);

      Assert.Contains("--name full-pod", result);
      Assert.Contains("--hostname my-host", result);
      Assert.Contains("--network my-net", result);
      Assert.Contains("--infra-image k8s.gcr.io/pause:3.5", result);
      Assert.Contains("--share ipc,net,uts", result);
      Assert.Contains("--label env=test", result);
      Assert.Contains("--dns 8.8.8.8", result);
      Assert.Contains("-p 8080:80", result);
    }

    [Fact]
    public void BuildCreateArgs_NoName_OmitsNameFlag()
    {
      var config = new PodCreateConfig();
      var result = InvokeBuildCreateArgs(config);
      Assert.Equal("pod create", result);
    }

    #endregion

    #region Reflection Helpers

    private static IList<PodInfo> InvokeParsePodList(string json)
    {
      var method = typeof(PodmanCliPodDriver).GetMethod(
          "ParsePodList",
          BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      return (IList<PodInfo>)method.Invoke(null, [json])!;
    }

    private static PodInspectResult InvokeParsePodInspect(string json)
    {
      var method = typeof(PodmanCliPodDriver).GetMethod(
          "ParsePodInspect",
          BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      return (PodInspectResult)method.Invoke(null, [json])!;
    }

    private static string InvokeBuildCreateArgs(PodCreateConfig config)
    {
      var method = typeof(PodmanCliPodDriver).GetMethod(
          "BuildCreateArgs",
          BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      return (string)method.Invoke(null, [config])!;
    }

    #endregion
  }
}
