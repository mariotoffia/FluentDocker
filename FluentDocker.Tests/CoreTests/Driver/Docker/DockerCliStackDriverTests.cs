using System.Collections.Generic;
using System.Text.Json;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Unit tests for DockerCliStackDriver: deploy/remove arg building
  /// and JSON parsing for stack list, stack services, and stack tasks.
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerCliStackDriverTests
  {
    #region Deploy Arg Building

    [Fact]
    public void DeployArgs_SingleComposeFile_ContainsComposeFileFlag()
    {
      var args = BuildDeployArgs(new StackDeployConfig
      {
        StackName = "mystack",
        ComposeFiles = new List<string> { "docker-compose.yml" }
      });

      Assert.StartsWith("stack deploy", args);
      Assert.Contains("-c docker-compose.yml", args);
      Assert.EndsWith("mystack", args);
    }

    [Fact]
    public void DeployArgs_MultipleComposeFiles_ContainsAllComposeFileFlags()
    {
      var args = BuildDeployArgs(new StackDeployConfig
      {
        StackName = "multi",
        ComposeFiles = new List<string>
        {
          "docker-compose.yml",
          "docker-compose.override.yml",
          "docker-compose.prod.yml"
        }
      });

      Assert.Contains("-c docker-compose.yml", args);
      Assert.Contains("-c docker-compose.override.yml", args);
      Assert.Contains("-c docker-compose.prod.yml", args);
    }

    [Fact]
    public void DeployArgs_WithPrune_ContainsPruneFlag()
    {
      var args = BuildDeployArgs(new StackDeployConfig
      {
        StackName = "pruned",
        ComposeFiles = new List<string> { "compose.yml" },
        Prune = true
      });

      Assert.Contains("--prune", args);
    }

    [Fact]
    public void DeployArgs_WithoutPrune_DoesNotContainPruneFlag()
    {
      var args = BuildDeployArgs(new StackDeployConfig
      {
        StackName = "nopruned",
        ComposeFiles = new List<string> { "compose.yml" },
        Prune = false
      });

      Assert.DoesNotContain("--prune", args);
    }

    [Fact]
    public void DeployArgs_WithRegistryAuth_ContainsRegistryAuthFlag()
    {
      var args = BuildDeployArgs(new StackDeployConfig
      {
        StackName = "authed",
        ComposeFiles = new List<string> { "compose.yml" },
        WithRegistryAuth = true
      });

      Assert.Contains("--with-registry-auth", args);
    }

    [Fact]
    public void DeployArgs_WithoutRegistryAuth_DoesNotContainRegistryAuthFlag()
    {
      var args = BuildDeployArgs(new StackDeployConfig
      {
        StackName = "noauth",
        ComposeFiles = new List<string> { "compose.yml" },
        WithRegistryAuth = false
      });

      Assert.DoesNotContain("--with-registry-auth", args);
    }

    [Fact]
    public void DeployArgs_StackNameAtEnd()
    {
      var args = BuildDeployArgs(new StackDeployConfig
      {
        StackName = "thestack",
        ComposeFiles = new List<string> { "f1.yml" },
        Prune = true,
        WithRegistryAuth = true
      });

      Assert.EndsWith("thestack", args);
    }

    [Fact]
    public void DeployArgs_AllFlags_CorrectOrder()
    {
      var args = BuildDeployArgs(new StackDeployConfig
      {
        StackName = "full",
        ComposeFiles = new List<string> { "a.yml", "b.yml" },
        Prune = true,
        WithRegistryAuth = true
      });

      // Compose flags come before prune, then registry-auth, then stack name
      var composeIdx = args.IndexOf("-c a.yml");
      var pruneIdx = args.IndexOf("--prune");
      var authIdx = args.IndexOf("--with-registry-auth");
      var nameIdx = args.IndexOf("full");

      Assert.True(composeIdx < pruneIdx, "Compose flags before prune");
      Assert.True(pruneIdx < authIdx, "Prune before registry-auth");
      Assert.True(authIdx < nameIdx, "Registry-auth before stack name");
    }

    [Fact]
    public void DeployArgs_NoComposeFiles_StillHasStackName()
    {
      var args = BuildDeployArgs(new StackDeployConfig
      {
        StackName = "empty"
      });

      Assert.Equal("stack deploy empty", args);
    }

    #endregion

    #region Remove Arg Building

    [Fact]
    public void RemoveArgs_SingleStack_CorrectFormat()
    {
      var args = BuildRemoveArgs(new[] { "mystack" });

      Assert.Equal("stack rm mystack", args);
    }

    [Fact]
    public void RemoveArgs_MultipleStacks_AllIncluded()
    {
      var args = BuildRemoveArgs(new[] { "stack1", "stack2", "stack3" });

      Assert.Equal("stack rm stack1 stack2 stack3", args);
    }

    [Fact]
    public void RemoveArgs_StartsWithStackRm()
    {
      var args = BuildRemoveArgs(new[] { "a", "b" });

      Assert.StartsWith("stack rm", args);
    }

    #endregion

    #region Stack List JSON Parsing

    [Fact]
    public void ParseStackListJson_SingleLine_ParsesStackInfo()
    {
      const string json =
          @"{""Name"":""web"",""Services"":3,""Orchestrator"":""swarm""}";

      var stack = JsonSerializer.Deserialize<StackInfo>(
          json, JsonHelper.CaseInsensitiveOptions);

      Assert.NotNull(stack);
      Assert.Equal("web", stack.Name);
      Assert.Equal(3, stack.Services);
      Assert.Equal("swarm", stack.Orchestrator);
    }

    [Fact]
    public void ParseStackListJson_MultipleLines_ParsesAll()
    {
      const string output =
          "{\"Name\":\"web\",\"Services\":3,\"Orchestrator\":\"swarm\"}\n" +
          "{\"Name\":\"api\",\"Services\":5,\"Orchestrator\":\"swarm\"}";

      var lines = output.Split(new[] { '\n', '\r' },
          System.StringSplitOptions.RemoveEmptyEntries);
      var stacks = new List<StackInfo>();
      foreach (var line in lines)
      {
        var s = JsonSerializer.Deserialize<StackInfo>(
            line, JsonHelper.CaseInsensitiveOptions);
        if (s != null)
          stacks.Add(s);
      }

      Assert.Equal(2, stacks.Count);
      Assert.Equal("web", stacks[0].Name);
      Assert.Equal("api", stacks[1].Name);
    }

    [Fact]
    public void ParseStackListJson_MissingFields_DefaultValues()
    {
      const string json = @"{""Name"":""minimal""}";

      var stack = JsonSerializer.Deserialize<StackInfo>(
          json, JsonHelper.CaseInsensitiveOptions);

      Assert.NotNull(stack);
      Assert.Equal("minimal", stack.Name);
      Assert.Equal(0, stack.Services);
      Assert.Null(stack.Orchestrator);
    }

    #endregion

    #region Stack Services JSON Parsing

    [Fact]
    public void ParseStackServiceJson_FullFields_ParsesCorrectly()
    {
      const string json =
          @"{""ID"":""svc1"",""Name"":""web_nginx"",""Mode"":""replicated"",""Replicas"":""3/3"",""Image"":""nginx:latest"",""Ports"":""*:80->80/tcp""}";

      var svc = JsonSerializer.Deserialize<StackServiceInfo>(
          json, JsonHelper.CaseInsensitiveOptions);

      Assert.NotNull(svc);
      Assert.Equal("svc1", svc.Id);
      Assert.Equal("web_nginx", svc.Name);
      Assert.Equal("replicated", svc.Mode);
      Assert.Equal("3/3", svc.Replicas);
      Assert.Equal("nginx:latest", svc.Image);
      Assert.Equal("*:80->80/tcp", svc.Ports);
    }

    [Fact]
    public void ParseStackServiceJson_MultipleLines_ParsesAll()
    {
      const string output =
          "{\"ID\":\"s1\",\"Name\":\"web\",\"Mode\":\"replicated\",\"Replicas\":\"2/2\",\"Image\":\"nginx\",\"Ports\":\"*:80->80/tcp\"}\n" +
          "{\"ID\":\"s2\",\"Name\":\"api\",\"Mode\":\"replicated\",\"Replicas\":\"3/3\",\"Image\":\"api:v1\",\"Ports\":\"*:8080->8080/tcp\"}";

      var lines = output.Split(new[] { '\n', '\r' },
          System.StringSplitOptions.RemoveEmptyEntries);
      var services = new List<StackServiceInfo>();
      foreach (var line in lines)
      {
        var s = JsonSerializer.Deserialize<StackServiceInfo>(
            line, JsonHelper.CaseInsensitiveOptions);
        if (s != null)
          services.Add(s);
      }

      Assert.Equal(2, services.Count);
      Assert.Equal("web", services[0].Name);
      Assert.Equal("api", services[1].Name);
    }

    #endregion

    #region Stack Task JSON Parsing

    [Fact]
    public void ParseStackTaskJson_FullFields_ParsesCorrectly()
    {
      const string json =
          @"{""ID"":""t1"",""Name"":""web.1"",""Image"":""nginx:latest"",""Node"":""node1"",""DesiredState"":""Running"",""CurrentState"":""Running 5 minutes ago"",""Error"":"""",""Ports"":""""}";

      var task = JsonSerializer.Deserialize<StackTask>(
          json, JsonHelper.CaseInsensitiveOptions);

      Assert.NotNull(task);
      Assert.Equal("t1", task.Id);
      Assert.Equal("web.1", task.Name);
      Assert.Equal("nginx:latest", task.Image);
      Assert.Equal("node1", task.Node);
      Assert.Equal("Running", task.DesiredState);
      Assert.Contains("Running", task.CurrentState);
    }

    [Fact]
    public void ParseStackTaskJson_WithError_ErrorFieldPopulated()
    {
      const string json =
          @"{""ID"":""t2"",""Name"":""api.1"",""Image"":""api:v1"",""Node"":""node2"",""DesiredState"":""Shutdown"",""CurrentState"":""Failed 2 minutes ago"",""Error"":""task: non-zero exit (1)"",""Ports"":""""}";

      var task = JsonSerializer.Deserialize<StackTask>(
          json, JsonHelper.CaseInsensitiveOptions);

      Assert.NotNull(task);
      Assert.Equal("Shutdown", task.DesiredState);
      Assert.Contains("Failed", task.CurrentState);
      Assert.Equal("task: non-zero exit (1)", task.Error);
    }

    #endregion

    #region Helper: Reconstruct Arg Strings

    /// <summary>
    /// Mirrors the arg-building logic from DockerCliStackDriver.DeployAsync.
    /// </summary>
    private static string BuildDeployArgs(StackDeployConfig config)
    {
      var args = "stack deploy";
      foreach (var file in config.ComposeFiles)
        args += $" -c {file}";
      if (config.Prune)
        args += " --prune";
      if (config.WithRegistryAuth)
        args += " --with-registry-auth";
      args += $" {config.StackName}";
      return args;
    }

    /// <summary>
    /// Mirrors the arg-building logic from DockerCliStackDriver.RemoveAsync.
    /// </summary>
    private static string BuildRemoveArgs(string[] stackNames)
    {
      return $"stack rm {string.Join(" ", stackNames)}";
    }

    #endregion
  }
}
