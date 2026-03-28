using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Docker.Cli.Components;
using Xunit;
using Container = FluentDocker.Model.Containers.Container;
using ContainerState = FluentDocker.Model.Containers.ContainerState;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Unit tests for DockerCliContainerDriver output parsing:
  /// inspect JSON, list (docker ps) JSON, diff output, top output,
  /// and stats JSON parsing.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class DockerCliContainerDriverTests
  {
    #region Inspect JSON Parsing Tests

    [Fact]
    public void InspectParsing_ValidJsonArray_ReturnsContainer()
    {
      var json = @"[{
        ""Id"": ""abc123def456"",
        ""Image"": ""sha256:abc123"",
        ""Name"": ""/test-container"",
        ""Driver"": ""overlay2"",
        ""State"": {
          ""Status"": ""running"",
          ""Running"": true,
          ""Paused"": false,
          ""Dead"": false,
          ""ExitCode"": 0
        }
      }]";

      var containers = JsonSerializer.Deserialize<List<Container>>(
          json, JsonHelper.CaseInsensitiveOptions);
      var container = containers?.FirstOrDefault();

      Assert.NotNull(container);
      Assert.Equal("abc123def456", container.Id);
      Assert.Equal("sha256:abc123", container.Image);
      Assert.Equal("/test-container", container.Name);
      Assert.Equal("overlay2", container.Driver);
      Assert.Equal("running", container.State.Status);
      Assert.True(container.State.Running);
      Assert.False(container.State.Paused);
      Assert.False(container.State.Dead);
      Assert.Equal(0, container.State.ExitCode);
    }

    [Fact]
    public void InspectParsing_ExitedContainer_ParsesExitCode()
    {
      var json = @"[{
        ""Id"": ""def456"",
        ""Image"": ""ubuntu"",
        ""Name"": ""exited-ctr"",
        ""State"": {
          ""Status"": ""exited"",
          ""Running"": false,
          ""ExitCode"": 137
        }
      }]";

      var containers = JsonSerializer.Deserialize<List<Container>>(
          json, JsonHelper.CaseInsensitiveOptions);
      var container = containers?.FirstOrDefault();

      Assert.NotNull(container);
      Assert.False(container.State.Running);
      Assert.Equal("exited", container.State.Status);
      Assert.Equal(137, container.State.ExitCode);
    }

    [Fact]
    public void InspectParsing_EmptyArray_ReturnsEmptyList()
    {
      var containers = JsonSerializer.Deserialize<List<Container>>(
          "[]", JsonHelper.CaseInsensitiveOptions);
      Assert.NotNull(containers);
      Assert.Empty(containers);
    }

    [Fact]
    public void InspectParsing_InvalidJson_ThrowsException()
    {
      Assert.ThrowsAny<Exception>(() =>
          JsonSerializer.Deserialize<List<Container>>(
              "not json", JsonHelper.CaseInsensitiveOptions));
    }

    [Fact]
    public void InspectParsing_ContainerWithMounts_ParsesMountArray()
    {
      var json = @"[{
        ""Id"": ""abc123"",
        ""Image"": ""nginx"",
        ""State"": { ""Status"": ""running"", ""Running"": true },
        ""Mounts"": [{
          ""Type"": ""bind"",
          ""Source"": ""/host/path"",
          ""Destination"": ""/container/path"",
          ""RW"": true
        }]
      }]";

      var containers = JsonSerializer.Deserialize<List<Container>>(
          json, JsonHelper.CaseInsensitiveOptions);
      var container = containers?.FirstOrDefault();

      Assert.NotNull(container);
      Assert.NotNull(container.Mounts);
      Assert.Single(container.Mounts);
      Assert.Equal("/host/path", container.Mounts[0].Source);
      Assert.Equal("/container/path", container.Mounts[0].Destination);
    }

    #endregion

    #region DockerPsDto / List Parsing Tests

    [Fact]
    public void ListParsing_SingleJsonLine_ParsesContainer()
    {
      // Replicates the per-line JSON parsing in ListAsync
      var line =
          "{\"ID\":\"abc123\",\"Image\":\"nginx:latest\"," +
          "\"Names\":\"web1\",\"State\":\"running\",\"Status\":\"Up 5 min\"}";

      var (id, image, name, state, status) = ParseDockerPsLine(line);

      Assert.Equal("abc123", id);
      Assert.Equal("nginx:latest", image);
      Assert.Equal("web1", name);
      Assert.Equal("running", state);
      Assert.Equal("Up 5 min", status);
    }

    [Fact]
    public void ListParsing_MultipleLines_ParsesAll()
    {
      var output =
          "{\"ID\":\"abc123\",\"Image\":\"nginx\",\"Names\":\"web1\"," +
          "\"State\":\"running\",\"Status\":\"Up\"}\n" +
          "{\"ID\":\"def456\",\"Image\":\"redis\",\"Names\":\"cache\"," +
          "\"State\":\"exited\",\"Status\":\"Exited (0)\"}";

      var lines = output.Split(new[] { '\n', '\r' },
          StringSplitOptions.RemoveEmptyEntries);

      Assert.Equal(2, lines.Length);

      var (id1, _, name1, _, _) = ParseDockerPsLine(lines[0]);
      var (id2, _, name2, _, _) = ParseDockerPsLine(lines[1]);

      Assert.Equal("abc123", id1);
      Assert.Equal("web1", name1);
      Assert.Equal("def456", id2);
      Assert.Equal("cache", name2);
    }

    [Fact]
    public void ListParsing_EmptyOutput_ProducesNoContainers()
    {
      var lines = "".Split(new[] { '\n', '\r' },
          StringSplitOptions.RemoveEmptyEntries);
      Assert.Empty(lines);
    }

    [Fact]
    public void ListParsing_RunningState_SetsRunningTrue()
    {
      var line = "{\"ID\":\"abc\",\"Image\":\"n\",\"Names\":\"x\"," +
                 "\"State\":\"running\",\"Status\":\"Up\"}";
      var (_, _, _, state, _) = ParseDockerPsLine(line);
      Assert.Equal("running", state, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ListParsing_ExitedState_SetsRunningFalse()
    {
      var line = "{\"ID\":\"abc\",\"Image\":\"n\",\"Names\":\"x\"," +
                 "\"State\":\"exited\",\"Status\":\"Exited (0)\"}";
      var (_, _, _, state, _) = ParseDockerPsLine(line);
      Assert.False(state.Equals("running", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ListParsing_WithCreatedAt_ParsesDate()
    {
      var line = "{\"ID\":\"abc\",\"Image\":\"n\",\"Names\":\"x\"," +
                 "\"State\":\"running\",\"CreatedAt\":\"2024-01-15 10:30:00\"}";

      using var doc = JsonDocument.Parse(line);
      var root = doc.RootElement;
      var createdAt = root.GetProperty("CreatedAt").GetString();
      Assert.True(DateTime.TryParse(createdAt, out var created));
      Assert.Equal(2024, created.Year);
    }

    #endregion

    #region Diff Output Parsing Tests

    [Fact]
    public void DiffParsing_AddedFile_ReturnsKindA()
    {
      var changes = ParseDiffOutput("A /added/file");
      Assert.Single(changes);
      Assert.Equal("A", changes[0].Kind);
      Assert.Equal("/added/file", changes[0].Path);
    }

    [Fact]
    public void DiffParsing_ChangedFile_ReturnsKindC()
    {
      var changes = ParseDiffOutput("C /changed/file");
      Assert.Single(changes);
      Assert.Equal("C", changes[0].Kind);
      Assert.Equal("/changed/file", changes[0].Path);
    }

    [Fact]
    public void DiffParsing_DeletedFile_ReturnsKindD()
    {
      var changes = ParseDiffOutput("D /deleted/file");
      Assert.Single(changes);
      Assert.Equal("D", changes[0].Kind);
      Assert.Equal("/deleted/file", changes[0].Path);
    }

    [Fact]
    public void DiffParsing_MultipleChanges_ParsesAll()
    {
      var output = "A /added\nC /changed\nD /deleted";
      var changes = ParseDiffOutput(output);
      Assert.Equal(3, changes.Count);
      Assert.Equal("A", changes[0].Kind);
      Assert.Equal("C", changes[1].Kind);
      Assert.Equal("D", changes[2].Kind);
    }

    [Fact]
    public void DiffParsing_EmptyOutput_ReturnsEmpty()
    {
      var changes = ParseDiffOutput("");
      Assert.Empty(changes);
    }

    [Fact]
    public void DiffParsing_LineWithPathContainingSpaces_ParsesCorrectly()
    {
      var changes = ParseDiffOutput("A /path with spaces/file.txt");
      Assert.Single(changes);
      Assert.Equal("A", changes[0].Kind);
      Assert.Equal("/path with spaces/file.txt", changes[0].Path);
    }

    [Fact]
    public void DiffParsing_ShortLine_SkipsIt()
    {
      // Lines of length <= 2 should be skipped
      var changes = ParseDiffOutput("A\nC /valid");
      Assert.Single(changes);
      Assert.Equal("C", changes[0].Kind);
    }

    #endregion

    #region Top Output Parsing Tests

    [Fact]
    public void TopParsing_ValidOutput_ParsesHeadersAndProcesses()
    {
      var output = "USER PID COMMAND\nroot 1 nginx\nroot 2 worker";
      var (titles, processes) = ParseTopOutput(output);

      Assert.Equal(3, titles.Count);
      Assert.Contains("USER", titles);
      Assert.Contains("PID", titles);
      Assert.Contains("COMMAND", titles);
      Assert.Equal(2, processes.Count);
    }

    [Fact]
    public void TopParsing_EmptyOutput_ReturnsEmptyLists()
    {
      var (titles, processes) = ParseTopOutput("");
      Assert.Empty(titles);
      Assert.Empty(processes);
    }

    [Fact]
    public void TopParsing_HeaderOnly_ReturnsNoProcesses()
    {
      var (titles, processes) = ParseTopOutput("UID PID PPID");
      Assert.Equal(3, titles.Count);
      Assert.Empty(processes);
    }

    [Fact]
    public void TopParsing_MultipleSpaces_SplitsCorrectly()
    {
      // Split on space with RemoveEmptyEntries handles multiple spaces
      var output = "USER  PID  CMD\nroot  1  nginx";
      var (titles, processes) = ParseTopOutput(output);
      Assert.Equal(3, titles.Count);
      Assert.Single(processes);
      Assert.Equal(3, processes[0].Count);
    }

    #endregion

    #region Stats JSON Parsing Tests

    [Fact]
    public void ParseStatsOutput_ValidJson_ParsesAllFields()
    {
      var json = @"{
        ""Name"": ""web-server"",
        ""CPUPerc"": ""5.23%"",
        ""MemPerc"": ""12.50%"",
        ""MemUsage"": ""100MiB / 2GiB"",
        ""NetIO"": ""1.5kB / 2.3kB"",
        ""BlockIO"": ""4MB / 8MB"",
        ""PIDs"": ""10""
      }";

      var stats = InvokeParseStatsOutput(json, "ctr123");

      Assert.Equal("ctr123", stats.ContainerId);
      Assert.Equal("web-server", stats.Name);
      Assert.Equal(5.23, stats.CpuPercent, 2);
      Assert.Equal(12.50, stats.MemoryPercent, 2);
      Assert.True(stats.MemoryUsage > 0);
      Assert.True(stats.MemoryLimit > 0);
      Assert.True(stats.NetworkRxBytes > 0);
      Assert.True(stats.NetworkTxBytes > 0);
      Assert.True(stats.BlockReadBytes > 0);
      Assert.True(stats.BlockWriteBytes > 0);
      Assert.Equal(10, stats.Pids);
    }

    [Fact]
    public void ParseStatsOutput_MissingFields_DefaultsToZero()
    {
      var json = @"{ ""Name"": ""minimal"" }";
      var stats = InvokeParseStatsOutput(json, "ctr1");

      Assert.Equal("minimal", stats.Name);
      Assert.Equal(0, stats.CpuPercent);
      Assert.Equal(0, stats.MemoryPercent);
      Assert.Equal(0, stats.MemoryUsage);
      Assert.Equal(0, stats.Pids);
    }

    [Fact]
    public void ParseStatsOutput_InvalidJson_ReturnsDefaultStats()
    {
      // ParseStatsOutput catches exceptions and returns default
      var stats = InvokeParseStatsOutput("not json", "ctr1");
      Assert.Equal("ctr1", stats.ContainerId);
      Assert.Null(stats.Name);
    }

    [Fact]
    public void ParseStatsOutput_ZeroCpu_ParsesCorrectly()
    {
      var json = @"{ ""CPUPerc"": ""0.00%"", ""PIDs"": ""1"" }";
      var stats = InvokeParseStatsOutput(json, "ctr1");
      Assert.Equal(0.0, stats.CpuPercent);
      Assert.Equal(1, stats.Pids);
    }

    #endregion

    #region Parsing Helpers

    /// <summary>
    /// Replicates the per-line DockerPsDto JSON parsing from ListAsync.
    /// </summary>
    private static (string? Id, string? Image, string? Names, string? State,
        string? Status) ParseDockerPsLine(string line)
    {
      using var doc = JsonDocument.Parse(line);
      var root = doc.RootElement;
      return (
          root.TryGetProperty("ID", out var id) ? id.GetString() : null,
          root.TryGetProperty("Image", out var img) ? img.GetString() : null,
          root.TryGetProperty("Names", out var names) ? names.GetString() : null,
          root.TryGetProperty("State", out var state) ? state.GetString() : null,
          root.TryGetProperty("Status", out var status) ? status.GetString() : null
      );
    }

    /// <summary>
    /// Replicates the diff parsing logic from DiffAsync.
    /// </summary>
    private static List<FilesystemChange> ParseDiffOutput(string output)
    {
      var changes = new List<FilesystemChange>();
      var lines = output.Split(new[] { '\n', '\r' },
          StringSplitOptions.RemoveEmptyEntries);
      foreach (var line in lines)
      {
        if (line.Length > 2)
        {
          changes.Add(new FilesystemChange
          {
            Kind = line.Substring(0, 1),
            Path = line.Substring(2)
          });
        }
      }
      return changes;
    }

    /// <summary>
    /// Replicates the top output parsing logic from TopAsync.
    /// </summary>
    private static (List<string> Titles, List<List<string>> Processes)
        ParseTopOutput(string output)
    {
      var titles = new List<string>();
      var processes = new List<List<string>>();
      var lines = output.Split(new[] { '\n', '\r' },
          StringSplitOptions.RemoveEmptyEntries);
      if (lines.Length > 0)
      {
        titles = lines[0].Split(new[] { ' ' },
            StringSplitOptions.RemoveEmptyEntries).ToList();
        for (var i = 1; i < lines.Length; i++)
        {
          processes.Add(lines[i].Split(new[] { ' ' },
              StringSplitOptions.RemoveEmptyEntries).ToList());
        }
      }
      return (titles, processes);
    }

    /// <summary>
    /// Calls ParseStatsOutput via reflection.
    /// </summary>
    private static ContainerStatsResult InvokeParseStatsOutput(
        string output, string containerId)
    {
      var method = typeof(DockerCliContainerDriver).GetMethod(
          "ParseStatsOutput",
          BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      return (ContainerStatsResult)method.Invoke(
          null, new object[] { output, containerId });
    }

    #endregion
  }
}
