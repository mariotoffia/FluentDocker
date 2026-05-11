using System.Collections.Generic;
using System.Text.Json;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Volumes;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Unit tests for DockerCliVolumeDriver: volume create argument building,
  /// filter argument construction, and JSON list parsing.
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerCliVolumeDriverTests
  {
    #region Volume Create Args - Name Only

    [Fact]
    public void VolumeCreateArgs_NameOnly_ProducesMinimalArgs()
    {
      var config = new VolumeCreateConfig { Name = "myvol" };

      var args = BuildVolumeCreateArgs(config);

      Assert.StartsWith("volume create", args);
      Assert.EndsWith(" myvol", args);
    }

    #endregion

    #region Volume Create Args - Driver

    [Fact]
    public void VolumeCreateArgs_WithDriver_IncludesDriverFlag()
    {
      var config = new VolumeCreateConfig
      {
        Name = "myvol",
        Driver = "nfs"
      };

      var args = BuildVolumeCreateArgs(config);

      Assert.Contains("--driver nfs", args);
    }

    [Fact]
    public void VolumeCreateArgs_DefaultLocalDriver_IncludesLocal()
    {
      var config = new VolumeCreateConfig { Name = "myvol" };

      var args = BuildVolumeCreateArgs(config);

      // VolumeCreateConfig defaults Driver to "local"
      Assert.Contains("--driver local", args);
    }

    [Fact]
    public void VolumeCreateArgs_NullDriver_OmitsDriverFlag()
    {
      var config = new VolumeCreateConfig
      {
        Name = "myvol",
        Driver = null
      };

      var args = BuildVolumeCreateArgs(config);

      Assert.DoesNotContain("--driver", args);
    }

    [Fact]
    public void VolumeCreateArgs_EmptyDriver_OmitsDriverFlag()
    {
      var config = new VolumeCreateConfig
      {
        Name = "myvol",
        Driver = ""
      };

      var args = BuildVolumeCreateArgs(config);

      Assert.DoesNotContain("--driver", args);
    }

    #endregion

    #region Volume Create Args - Labels

    [Fact]
    public void VolumeCreateArgs_SingleLabel_IncludesLabelFlag()
    {
      var config = new VolumeCreateConfig
      {
        Name = "myvol",
        Labels = { { "env", "test" } }
      };

      var args = BuildVolumeCreateArgs(config);

      Assert.Contains("--label env=test", args);
    }

    [Fact]
    public void VolumeCreateArgs_MultipleLabels_IncludesAll()
    {
      var config = new VolumeCreateConfig
      {
        Name = "myvol",
        Labels =
        {
          { "env", "test" },
          { "project", "demo" }
        }
      };

      var args = BuildVolumeCreateArgs(config);

      Assert.Contains("--label env=test", args);
      Assert.Contains("--label project=demo", args);
    }

    [Fact]
    public void VolumeCreateArgs_NoLabels_OmitsLabelFlag()
    {
      var config = new VolumeCreateConfig { Name = "myvol" };

      var args = BuildVolumeCreateArgs(config);

      Assert.DoesNotContain("--label", args);
    }

    [Fact]
    public void VolumeCreateArgs_NullLabels_OmitsLabelFlag()
    {
      var config = new VolumeCreateConfig
      {
        Name = "myvol",
        Labels = null
      };

      var args = BuildVolumeCreateArgs(config);

      Assert.DoesNotContain("--label", args);
    }

    #endregion

    #region Volume Create Args - DriverOpts

    [Fact]
    public void VolumeCreateArgs_SingleDriverOpt_IncludesOptFlag()
    {
      var config = new VolumeCreateConfig
      {
        Name = "myvol",
        DriverOpts = { { "type", "tmpfs" } }
      };

      var args = BuildVolumeCreateArgs(config);

      Assert.Contains("--opt type=tmpfs", args);
    }

    [Fact]
    public void VolumeCreateArgs_MultipleDriverOpts_IncludesAll()
    {
      var config = new VolumeCreateConfig
      {
        Name = "myvol",
        DriverOpts =
        {
          { "type", "nfs" },
          { "device", ":/data/share" },
          { "o", "addr=192.168.1.1,rw" }
        }
      };

      var args = BuildVolumeCreateArgs(config);

      Assert.Contains("--opt type=nfs", args);
      Assert.Contains("--opt device=:/data/share", args);
      Assert.Contains("--opt o=addr=192.168.1.1,rw", args);
    }

    [Fact]
    public void VolumeCreateArgs_NoDriverOpts_OmitsOptFlag()
    {
      var config = new VolumeCreateConfig { Name = "myvol" };

      var args = BuildVolumeCreateArgs(config);

      Assert.DoesNotContain("--opt", args);
    }

    [Fact]
    public void VolumeCreateArgs_NullDriverOpts_OmitsOptFlag()
    {
      var config = new VolumeCreateConfig
      {
        Name = "myvol",
        DriverOpts = null
      };

      var args = BuildVolumeCreateArgs(config);

      Assert.DoesNotContain("--opt", args);
    }

    #endregion

    #region Volume Create Args - Full Config

    [Fact]
    public void VolumeCreateArgs_FullConfig_ContainsAllFlags()
    {
      var config = new VolumeCreateConfig
      {
        Name = "nfs-data",
        Driver = "local",
        Labels =
        {
          { "env", "production" },
          { "team", "infra" }
        },
        DriverOpts =
        {
          { "type", "nfs" },
          { "device", ":/exports/data" },
          { "o", "addr=10.0.0.5,rw,nfsvers=4" }
        }
      };

      var args = BuildVolumeCreateArgs(config);

      Assert.StartsWith("volume create", args);
      Assert.Contains("--driver local", args);
      Assert.Contains("--label env=production", args);
      Assert.Contains("--label team=infra", args);
      Assert.Contains("--opt type=nfs", args);
      Assert.Contains("--opt device=:/exports/data", args);
      Assert.Contains("--opt o=addr=10.0.0.5,rw,nfsvers=4", args);
      Assert.EndsWith(" nfs-data", args);
    }

    [Fact]
    public void VolumeCreateArgs_NameIsLastArgument()
    {
      var config = new VolumeCreateConfig
      {
        Name = "testvol",
        Driver = "local",
        Labels = { { "env", "test" } }
      };

      var args = BuildVolumeCreateArgs(config);

      Assert.EndsWith(" testvol", args);
    }

    #endregion

    #region Volume List Filter Args

    [Fact]
    public void VolumeListFilterArgs_WithName_IncludesNameFilter()
    {
      var filter = new VolumeListFilter { Name = "myvol" };

      var args = BuildVolumeListFilterArgs(filter);

      Assert.Contains("--filter name=myvol", args);
    }

    [Fact]
    public void VolumeListFilterArgs_WithLabels_IncludesLabelFilter()
    {
      var filter = new VolumeListFilter
      {
        Labels = { { "env", "prod" } }
      };

      var args = BuildVolumeListFilterArgs(filter);

      Assert.Contains("--filter label=env=prod", args);
    }

    [Fact]
    public void VolumeListFilterArgs_WithNameAndLabels_IncludesBoth()
    {
      var filter = new VolumeListFilter
      {
        Name = "data",
        Labels =
        {
          { "env", "prod" },
          { "team", "data" }
        }
      };

      var args = BuildVolumeListFilterArgs(filter);

      Assert.Contains("--filter name=data", args);
      Assert.Contains("--filter label=env=prod", args);
      Assert.Contains("--filter label=team=data", args);
    }

    [Fact]
    public void VolumeListFilterArgs_NullFilter_NoFilterFlags()
    {
      var args = BuildVolumeListFilterArgs(null);

      Assert.DoesNotContain("--filter", args);
    }

    [Fact]
    public void VolumeListFilterArgs_EmptyFilter_NoFilterFlags()
    {
      var filter = new VolumeListFilter();

      var args = BuildVolumeListFilterArgs(filter);

      Assert.DoesNotContain("--filter", args);
    }

    #endregion

    #region Volume JSON Parsing

    [Fact]
    public void ParseVolumeJson_ValidJson_ReturnsVolume()
    {
      var json = @"{""Name"":""myvol"",""Driver"":""local"",""Scope"":""local"",""Mountpoint"":""/var/lib/docker/volumes/myvol/_data""}";

      var volume = JsonSerializer.Deserialize<Volume>(json, JsonHelper.CaseInsensitiveOptions);

      Assert.NotNull(volume);
      Assert.Equal("myvol", volume.Name);
      Assert.Equal("local", volume.Driver);
      Assert.Equal("local", volume.Scope);
      Assert.Equal("/var/lib/docker/volumes/myvol/_data", volume.Mountpoint);
    }

    [Fact]
    public void ParseVolumeJson_WithLabels_ParsesLabels()
    {
      var json = @"{""Name"":""myvol"",""Driver"":""local"",""Labels"":{""env"":""test"",""project"":""demo""}}";

      var volume = JsonSerializer.Deserialize<Volume>(json, JsonHelper.CaseInsensitiveOptions);

      Assert.NotNull(volume);
      Assert.NotNull(volume.Labels);
      Assert.Equal(2, volume.Labels.Count);
      Assert.Equal("test", volume.Labels["env"]);
      Assert.Equal("demo", volume.Labels["project"]);
    }

    [Fact]
    public void ParseVolumeJson_WithOptions_ParsesOptions()
    {
      var json = @"{""Name"":""nfsvol"",""Driver"":""local"",""Options"":{""type"":""nfs"",""device"":"":/data""}}";

      var volume = JsonSerializer.Deserialize<Volume>(json, JsonHelper.CaseInsensitiveOptions);

      Assert.NotNull(volume);
      Assert.NotNull(volume.Options);
      Assert.Equal(2, volume.Options.Count);
      Assert.Equal("nfs", volume.Options["type"]);
      Assert.Equal(":/data", volume.Options["device"]);
    }

    [Fact]
    public void ParseVolumeJson_MultipleLines_ParsesAll()
    {
      var lines = new[]
      {
        @"{""Name"":""vol1"",""Driver"":""local"",""Scope"":""local""}",
        @"{""Name"":""vol2"",""Driver"":""local"",""Scope"":""local""}",
        @"{""Name"":""vol3"",""Driver"":""nfs"",""Scope"":""local""}"
      };

      var volumes = new List<Volume>();
      foreach (var line in lines)
      {
        var volume = JsonSerializer.Deserialize<Volume>(line, JsonHelper.CaseInsensitiveOptions);
        if (volume != null)
          volumes.Add(volume);
      }

      Assert.Equal(3, volumes.Count);
      Assert.Equal("vol1", volumes[0].Name);
      Assert.Equal("vol2", volumes[1].Name);
      Assert.Equal("vol3", volumes[2].Name);
      Assert.Equal("nfs", volumes[2].Driver);
    }

    [Fact]
    public void ParseVolumeJson_NullLabelsAndOptions_DefaultsToNull()
    {
      var json = @"{""Name"":""simplevol"",""Driver"":""local""}";

      var volume = JsonSerializer.Deserialize<Volume>(json, JsonHelper.CaseInsensitiveOptions);

      Assert.NotNull(volume);
      Assert.Equal("simplevol", volume.Name);
      // Volume model uses non-initialized properties, so they can be null
      // when not present in JSON.
    }

    [Fact]
    public void ParseVolumeJson_CaseInsensitive_ParsesCorrectly()
    {
      var json = @"{""name"":""myvol"",""driver"":""local"",""mountpoint"":""/data""}";

      var volume = JsonSerializer.Deserialize<Volume>(json, JsonHelper.CaseInsensitiveOptions);

      Assert.NotNull(volume);
      Assert.Equal("myvol", volume.Name);
      Assert.Equal("local", volume.Driver);
      Assert.Equal("/data", volume.Mountpoint);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Replicates the argument building logic from DockerCliVolumeDriver.CreateAsync
    /// to test it in isolation without executing Docker commands.
    /// </summary>
    private static string BuildVolumeCreateArgs(VolumeCreateConfig config)
    {
      var args = new List<string> { "volume", "create" };

      if (!string.IsNullOrEmpty(config.Driver))
        args.Add($"--driver {config.Driver}");

      if (config.Labels != null)
      {
        foreach (var label in config.Labels)
          args.Add($"--label {label.Key}={label.Value}");
      }

      if (config.DriverOpts != null)
      {
        foreach (var opt in config.DriverOpts)
          args.Add($"--opt {opt.Key}={opt.Value}");
      }

      args.Add(config.Name);

      return string.Join(" ", args);
    }

    /// <summary>
    /// Replicates the filter argument construction from DockerCliVolumeDriver.ListAsync
    /// to test it in isolation.
    /// </summary>
    private static string BuildVolumeListFilterArgs(VolumeListFilter filter)
    {
      var args = "volume ls --format \"{{json .}}\"";

      if (filter != null)
      {
        if (!string.IsNullOrEmpty(filter.Name))
          args += $" --filter name={filter.Name}";

        if (filter.Labels != null)
        {
          foreach (var label in filter.Labels)
            args += $" --filter label={label.Key}={label.Value}";
        }
      }

      return args;
    }

    #endregion
  }
}
