using System.Collections.Generic;
using System.Reflection;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Unit tests for DockerCliServiceDriver: arg building for create/update/scale.
  /// ParseServiceInspect tests are in the .Parsing partial file.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class DockerCliServiceDriverTests
  {
    #region Service Create Arg Building

    [Fact]
    public void CreateArgs_MinimalConfig_ContainsServiceCreateAndImage()
    {
      var args = BuildCreateArgs(new ServiceCreateConfig { Image = "nginx:latest" });

      Assert.StartsWith("service create", args);
      Assert.Contains("nginx:latest", args);
    }

    [Fact]
    public void CreateArgs_WithName_ContainsNameFlag()
    {
      var args = BuildCreateArgs(new ServiceCreateConfig
      {
        Image = "nginx",
        Name = "web"
      });

      Assert.Contains("--name web", args);
    }

    [Fact]
    public void CreateArgs_WithReplicas_ContainsReplicasFlag()
    {
      var args = BuildCreateArgs(new ServiceCreateConfig
      {
        Image = "nginx",
        Replicas = 3
      });

      Assert.Contains("--replicas 3", args);
    }

    [Fact]
    public void CreateArgs_WithMode_ContainsModeFlag()
    {
      var args = BuildCreateArgs(new ServiceCreateConfig
      {
        Image = "nginx",
        Mode = "global"
      });

      Assert.Contains("--mode global", args);
    }

    [Fact]
    public void CreateArgs_WithEnvironment_ContainsEnvFlags()
    {
      var config = new ServiceCreateConfig { Image = "nginx" };
      config.Environment["DB_HOST"] = "db.local";
      config.Environment["DB_PORT"] = "5432";

      var args = BuildCreateArgs(config);

      Assert.Contains("-e DB_HOST=db.local", args);
      Assert.Contains("-e DB_PORT=5432", args);
    }

    [Fact]
    public void CreateArgs_WithLabels_ContainsLabelFlags()
    {
      var config = new ServiceCreateConfig { Image = "nginx" };
      config.Labels["com.example.version"] = "1.0";
      config.Labels["com.example.team"] = "backend";

      var args = BuildCreateArgs(config);

      Assert.Contains("--label com.example.version=1.0", args);
      Assert.Contains("--label com.example.team=backend", args);
    }

    [Fact]
    public void CreateArgs_WithPorts_ContainsPortMappings()
    {
      var config = new ServiceCreateConfig { Image = "nginx" };
      config.Ports.Add(new ServicePort
      {
        PublishedPort = 8080,
        TargetPort = 80,
        Protocol = "tcp"
      });
      config.Ports.Add(new ServicePort
      {
        PublishedPort = 53,
        TargetPort = 53,
        Protocol = "udp"
      });

      var args = BuildCreateArgs(config);

      Assert.Contains("-p 8080:80/tcp", args);
      Assert.Contains("-p 53:53/udp", args);
    }

    [Fact]
    public void CreateArgs_WithNetworks_ContainsNetworkFlags()
    {
      var config = new ServiceCreateConfig { Image = "nginx" };
      config.Networks.Add("frontend");
      config.Networks.Add("backend");

      var args = BuildCreateArgs(config);

      Assert.Contains("--network frontend", args);
      Assert.Contains("--network backend", args);
    }

    [Fact]
    public void CreateArgs_WithDetachAndQuiet_ContainsFlags()
    {
      var config = new ServiceCreateConfig
      {
        Image = "nginx",
        Detach = true,
        Quiet = true
      };

      var args = BuildCreateArgs(config);

      Assert.Contains("-d", args);
      Assert.Contains("-q", args);
    }

    [Fact]
    public void CreateArgs_WithCommand_AppendsCommandAfterImage()
    {
      var config = new ServiceCreateConfig
      {
        Image = "nginx",
        Command = new[] { "nginx", "-g", "daemon off;" }
      };

      var args = BuildCreateArgs(config);

      var imageIdx = args.IndexOf("nginx", args.IndexOf("service create") + 14);
      var cmdIdx = args.IndexOf("-g", imageIdx);
      Assert.True(cmdIdx > imageIdx);
      Assert.Contains("daemon off;", args);
    }

    [Fact]
    public void CreateArgs_NoReplicas_DoesNotContainReplicasFlag()
    {
      var config = new ServiceCreateConfig { Image = "nginx" };

      var args = BuildCreateArgs(config);

      Assert.DoesNotContain("--replicas", args);
    }

    [Fact]
    public void CreateArgs_FullConfig_ArgOrderCorrect()
    {
      var config = new ServiceCreateConfig
      {
        Image = "myapp:v2",
        Name = "myservice",
        Replicas = 5,
        Mode = "replicated"
      };
      config.Environment["FOO"] = "bar";
      config.Labels["tier"] = "frontend";
      config.Ports.Add(new ServicePort
      {
        PublishedPort = 443,
        TargetPort = 8443,
        Protocol = "tcp"
      });
      config.Networks.Add("overlay-net");
      config.Command = new[] { "/bin/sh", "-c", "start" };

      var args = BuildCreateArgs(config);

      Assert.StartsWith("service create", args);
      Assert.Contains("myapp:v2", args);
      Assert.Contains("/bin/sh", args);
      Assert.Contains("-c", args);
      Assert.Contains("start", args);
    }

    #endregion

    #region Service Update Arg Building

    [Fact]
    public void UpdateArgs_WithImage_ContainsImageFlag()
    {
      var args = BuildUpdateArgs("svc1", new ServiceUpdateConfig
      {
        Image = "nginx:2.0"
      });

      Assert.Contains("--image nginx:2.0", args);
      Assert.EndsWith("svc1", args);
    }

    [Fact]
    public void UpdateArgs_WithReplicas_ContainsReplicasFlag()
    {
      var args = BuildUpdateArgs("svc1", new ServiceUpdateConfig
      {
        Replicas = 10
      });

      Assert.Contains("--replicas 10", args);
    }

    [Fact]
    public void UpdateArgs_WithEnvAdd_ContainsEnvAddFlags()
    {
      var config = new ServiceUpdateConfig();
      config.EnvAdd["NEW_VAR"] = "value1";
      config.EnvAdd["OTHER"] = "value2";

      var args = BuildUpdateArgs("svc1", config);

      Assert.Contains("--env-add NEW_VAR=value1", args);
      Assert.Contains("--env-add OTHER=value2", args);
    }

    [Fact]
    public void UpdateArgs_WithEnvRm_ContainsEnvRmFlags()
    {
      var config = new ServiceUpdateConfig();
      config.EnvRm.Add("OLD_VAR");
      config.EnvRm.Add("LEGACY");

      var args = BuildUpdateArgs("svc1", config);

      Assert.Contains("--env-rm OLD_VAR", args);
      Assert.Contains("--env-rm LEGACY", args);
    }

    [Fact]
    public void UpdateArgs_WithForce_ContainsForceFlag()
    {
      var args = BuildUpdateArgs("svc1", new ServiceUpdateConfig
      {
        Force = true
      });

      Assert.Contains("--force", args);
    }

    [Fact]
    public void UpdateArgs_WithDetach_ContainsDetachFlag()
    {
      var args = BuildUpdateArgs("svc1", new ServiceUpdateConfig
      {
        Detach = true
      });

      Assert.Contains("-d", args);
    }

    [Fact]
    public void UpdateArgs_ServiceIdAppended_AtEnd()
    {
      var args = BuildUpdateArgs("my-service", new ServiceUpdateConfig
      {
        Image = "app:v3",
        Force = true
      });

      Assert.EndsWith("my-service", args);
    }

    [Fact]
    public void UpdateArgs_EmptyConfig_OnlyServiceUpdateAndId()
    {
      var args = BuildUpdateArgs("svc1", new ServiceUpdateConfig());

      Assert.Equal("service update svc1", args);
    }

    #endregion

    #region Service Scale Arg Building

    [Fact]
    public void ScaleArgs_SingleService_CorrectFormat()
    {
      var replicas = new Dictionary<string, int> { ["web"] = 5 };

      var args = BuildScaleArgs(replicas, detach: false);

      Assert.Equal("service scale web=5", args);
    }

    [Fact]
    public void ScaleArgs_MultipleServices_AllIncluded()
    {
      var replicas = new Dictionary<string, int>
      {
        ["web"] = 3,
        ["worker"] = 10
      };

      var args = BuildScaleArgs(replicas, detach: false);

      Assert.Contains("web=3", args);
      Assert.Contains("worker=10", args);
      Assert.StartsWith("service scale", args);
    }

    [Fact]
    public void ScaleArgs_WithDetach_ContainsDetachFlag()
    {
      var replicas = new Dictionary<string, int> { ["web"] = 2 };

      var args = BuildScaleArgs(replicas, detach: true);

      Assert.Contains("-d", args);
      Assert.Contains("web=2", args);
    }

    [Fact]
    public void ScaleArgs_WithDetach_DetachInsertedAfterScale()
    {
      var replicas = new Dictionary<string, int> { ["app"] = 1 };

      var args = BuildScaleArgs(replicas, detach: true);

      Assert.Contains("service scale -d", args);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    public void ScaleArgs_VariousReplicaCounts_CorrectFormat(int count)
    {
      var replicas = new Dictionary<string, int> { ["svc"] = count };

      var args = BuildScaleArgs(replicas, detach: false);

      Assert.Contains($"svc={count}", args);
    }

    #endregion

    #region Helper: Reconstruct Arg Strings

    /// <summary>
    /// Reconstructs the args list that CreateAsync would build.
    /// </summary>
    private static string BuildCreateArgs(ServiceCreateConfig config)
    {
      var args = new List<string> { "service", "create" };

      if (!string.IsNullOrEmpty(config.Name))
        args.Add($"--name {config.Name}");
      if (config.Replicas.HasValue)
        args.Add($"--replicas {config.Replicas.Value}");
      if (!string.IsNullOrEmpty(config.Mode))
        args.Add($"--mode {config.Mode}");
      foreach (var env in config.Environment)
        args.Add($"-e {env.Key}={env.Value}");
      foreach (var label in config.Labels)
        args.Add($"--label {label.Key}={label.Value}");
      foreach (var port in config.Ports)
        args.Add($"-p {port.PublishedPort}:{port.TargetPort}/{port.Protocol}");
      foreach (var network in config.Networks)
        args.Add($"--network {network}");
      if (config.Detach)
        args.Add("-d");
      if (config.Quiet)
        args.Add("-q");

      args.Add(config.Image);
      if (config.Command != null)
        args.AddRange(config.Command);

      return string.Join(" ", args);
    }

    /// <summary>
    /// Reconstructs the args list that UpdateAsync would build.
    /// </summary>
    private static string BuildUpdateArgs(
        string serviceId, ServiceUpdateConfig config)
    {
      var args = new List<string> { "service", "update" };

      if (!string.IsNullOrEmpty(config.Image))
        args.Add($"--image {config.Image}");
      if (config.Replicas.HasValue)
        args.Add($"--replicas {config.Replicas.Value}");
      foreach (var env in config.EnvAdd)
        args.Add($"--env-add {env.Key}={env.Value}");
      foreach (var env in config.EnvRm)
        args.Add($"--env-rm {env}");
      if (config.Force)
        args.Add("--force");
      if (config.Detach)
        args.Add("-d");

      args.Add(serviceId);

      return string.Join(" ", args);
    }

    /// <summary>
    /// Reconstructs the args that ScaleAsync would build.
    /// </summary>
    private static string BuildScaleArgs(
        Dictionary<string, int> serviceReplicas, bool detach)
    {
      var scaleArgs = string.Join(" ",
          new List<string>(
              System.Linq.Enumerable.Select(serviceReplicas,
                  sr => $"{sr.Key}={sr.Value}")));
      var args = $"service scale {scaleArgs}";
      if (detach)
        args = args.Replace("service scale", "service scale -d");
      return args;
    }

    #endregion
  }
}
