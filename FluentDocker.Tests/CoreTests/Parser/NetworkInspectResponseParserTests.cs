using System;
using System.Reflection;
using FluentDocker.Executors;
using FluentDocker.Executors.Parsers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Parser
{
  [Trait("Category", "Unit")]
  public class NetworkInspectResponseParserTests
  {
    [Fact]
    public void Process_WithExtendedIpamFields_ParsesOptionsAndConfig()
    {
      var stdOut = @"[
{
  ""Name"": ""fd-net"",
  ""Id"": ""net-1"",
  ""IPAM"": {
    ""Driver"": ""default"",
    ""Options"": {
      ""foo"": ""bar""
    },
    ""Config"": [
      {
        ""Subnet"": ""10.10.0.0/16"",
        ""Gateway"": ""10.10.0.1"",
        ""IPRange"": ""10.10.1.0/24"",
        ""AuxiliaryAddresses"": {
          ""host1"": ""10.10.1.10""
        }
      }
    ]
  }
}
]";

      var parser = new NetworkInspectResponseParser();
      var response = parser.Process(CreateProcessExecutionResult(
          "docker network inspect fd-net", stdOut, "", 0)).Response;

      Assert.True(response.Success);
      Assert.NotNull(response.Data);
      Assert.NotNull(response.Data.IPAM);
      Assert.Equal("default", response.Data.IPAM.Driver);
      Assert.NotNull(response.Data.IPAM.Options);
      Assert.Equal("bar", response.Data.IPAM.Options["foo"]);
      Assert.Single(response.Data.IPAM.Config);
      Assert.Equal("10.10.1.0/24", response.Data.IPAM.Config[0].IPRange);
      Assert.NotNull(response.Data.IPAM.Config[0].AuxiliaryAddresses);
      Assert.Equal("10.10.1.10", response.Data.IPAM.Config[0].AuxiliaryAddresses["host1"]);
    }

    private static ProcessExecutionResult CreateProcessExecutionResult(
        string command, string stdOut, string stdErr, int exitCode)
    {
      var ctorArgs = new object[] { command, stdOut, stdErr, exitCode };
      return (ProcessExecutionResult)Activator.CreateInstance(
          typeof(ProcessExecutionResult),
          BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.CreateInstance,
          null, ctorArgs, null, null);
    }
  }
}
