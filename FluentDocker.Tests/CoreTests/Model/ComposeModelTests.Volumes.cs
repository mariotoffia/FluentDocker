using System.Collections.Generic;
using FluentDocker.Model.Compose;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  [Trait("Category", "Unit")]
  public partial class ComposeModelTests
  {
    #region ShortServiceVolumeDefinition Tests

    [Fact]
    public void ShortServiceVolumeDefinition_DefaultConstruction_HasNullEntry()
    {
      var vol = new ShortServiceVolumeDefinition();
      Assert.Null(vol.Entry);
    }

    [Fact]
    public void ShortServiceVolumeDefinition_ImplementsIServiceVolumeDefinition()
    {
      IServiceVolumeDefinition vol = new ShortServiceVolumeDefinition
      {
        Entry = "/var/lib/mysql"
      };
      Assert.IsType<ShortServiceVolumeDefinition>(vol);
    }

    [Theory]
    [InlineData("/var/lib/mysql")]
    [InlineData("/opt/data:/var/lib/mysql")]
    [InlineData("./cache:/tmp/cache")]
    [InlineData("~/configs:/etc/configs/:ro")]
    [InlineData("datavolume:/var/lib/mysql")]
    public void ShortServiceVolumeDefinition_Entry_AcceptsAllFormats(string entry)
    {
      var vol = new ShortServiceVolumeDefinition { Entry = entry };
      Assert.Equal(entry, vol.Entry);
    }

    [Fact]
    public void ShortServiceVolumeDefinition_ContainerPathOnly_StoresEntry()
    {
      var vol = new ShortServiceVolumeDefinition { Entry = "/var/lib/mysql" };
      Assert.Equal("/var/lib/mysql", vol.Entry);
    }

    [Fact]
    public void ShortServiceVolumeDefinition_AbsolutePathMapping_StoresEntry()
    {
      var vol = new ShortServiceVolumeDefinition
      {
        Entry = "/opt/data:/var/lib/mysql"
      };
      Assert.Equal("/opt/data:/var/lib/mysql", vol.Entry);
    }

    [Fact]
    public void ShortServiceVolumeDefinition_RelativePath_StoresEntry()
    {
      var vol = new ShortServiceVolumeDefinition
      {
        Entry = "./cache:/tmp/cache"
      };
      Assert.Equal("./cache:/tmp/cache", vol.Entry);
    }

    [Fact]
    public void ShortServiceVolumeDefinition_ReadOnlyMount_StoresEntry()
    {
      var vol = new ShortServiceVolumeDefinition
      {
        Entry = "~/configs:/etc/configs/:ro"
      };
      Assert.Equal("~/configs:/etc/configs/:ro", vol.Entry);
    }

    [Fact]
    public void ShortServiceVolumeDefinition_NamedVolume_StoresEntry()
    {
      var vol = new ShortServiceVolumeDefinition
      {
        Entry = "datavolume:/var/lib/mysql"
      };
      Assert.Equal("datavolume:/var/lib/mysql", vol.Entry);
    }

    [Fact]
    public void ShortServiceVolumeDefinition_ReadWriteMode_StoresEntry()
    {
      var vol = new ShortServiceVolumeDefinition
      {
        Entry = "./data:/app/data:rw"
      };
      Assert.Equal("./data:/app/data:rw", vol.Entry);
    }

    #endregion

    #region LongServiceVolumeDefinition Tests

    [Fact]
    public void LongServiceVolumeDefinition_DefaultConstruction_HasExpectedDefaults()
    {
      var vol = new LongServiceVolumeDefinition();

      Assert.Null(vol.Source);
      Assert.Null(vol.Target);
      Assert.Equal(VolumeType.Volume, vol.Type);
      Assert.False(vol.IsReadOnly);
      Assert.NotNull(vol.Options);
      Assert.Empty(vol.Options);
    }

    [Fact]
    public void LongServiceVolumeDefinition_SetProperties_RoundTrips()
    {
      var vol = new LongServiceVolumeDefinition
      {
        Source = "mydata",
        Target = "/data",
        Type = VolumeType.Volume,
        IsReadOnly = false
      };

      Assert.Equal("mydata", vol.Source);
      Assert.Equal("/data", vol.Target);
      Assert.Equal(VolumeType.Volume, vol.Type);
      Assert.False(vol.IsReadOnly);
    }

    [Fact]
    public void LongServiceVolumeDefinition_BindMount_RoundTrips()
    {
      var vol = new LongServiceVolumeDefinition
      {
        Source = "/opt/data",
        Target = "/var/lib/mysql",
        Type = VolumeType.Bind,
        IsReadOnly = false
      };
      vol.Options["propagation"] = "rprivate";

      Assert.Equal(VolumeType.Bind, vol.Type);
      Assert.Equal("/opt/data", vol.Source);
      Assert.Equal("/var/lib/mysql", vol.Target);
      Assert.Single(vol.Options);
      Assert.Equal("rprivate", vol.Options["propagation"]);
    }

    [Fact]
    public void LongServiceVolumeDefinition_TmpFsMount_RoundTrips()
    {
      var vol = new LongServiceVolumeDefinition
      {
        Target = "/tmp",
        Type = VolumeType.TmpFs
      };
      vol.Options["size"] = "1048576";

      Assert.Equal(VolumeType.TmpFs, vol.Type);
      Assert.Equal("/tmp", vol.Target);
      Assert.Null(vol.Source);
      Assert.Equal("1048576", vol.Options["size"]);
    }

    [Fact]
    public void LongServiceVolumeDefinition_ReadOnly_RoundTrips()
    {
      var vol = new LongServiceVolumeDefinition
      {
        Source = "config",
        Target = "/etc/app",
        Type = VolumeType.Volume,
        IsReadOnly = true
      };

      Assert.True(vol.IsReadOnly);
    }

    [Fact]
    public void LongServiceVolumeDefinition_VolumeWithNoCopy_RoundTrips()
    {
      var vol = new LongServiceVolumeDefinition
      {
        Source = "dbdata",
        Target = "/var/lib/db",
        Type = VolumeType.Volume
      };
      vol.Options["nocopy"] = "true";

      Assert.Equal("true", vol.Options["nocopy"]);
    }

    [Fact]
    public void LongServiceVolumeDefinition_ImplementsIServiceVolumeDefinition()
    {
      IServiceVolumeDefinition vol = new LongServiceVolumeDefinition
      {
        Source = "mydata",
        Target = "/data"
      };
      Assert.IsType<LongServiceVolumeDefinition>(vol);
    }

    [Fact]
    public void LongServiceVolumeDefinition_DefaultType_IsVolume()
    {
      var vol = new LongServiceVolumeDefinition();
      Assert.Equal(VolumeType.Volume, vol.Type);
    }

    [Theory]
    [InlineData(VolumeType.Volume)]
    [InlineData(VolumeType.Bind)]
    [InlineData(VolumeType.TmpFs)]
    public void LongServiceVolumeDefinition_AllVolumeTypes_CanBeAssigned(
      VolumeType type)
    {
      var vol = new LongServiceVolumeDefinition { Type = type };
      Assert.Equal(type, vol.Type);
    }

    #endregion

    #region Volumes in Service Context Tests

    [Fact]
    public void ServiceVolumes_MixedShortAndLong_CanBeFiltered()
    {
      var svc = new ComposeServiceDefinition();

      svc.Volumes.Add(new ShortServiceVolumeDefinition
      {
        Entry = "/var/lib/mysql"
      });
      svc.Volumes.Add(new ShortServiceVolumeDefinition
      {
        Entry = "./cache:/tmp/cache"
      });
      svc.Volumes.Add(new LongServiceVolumeDefinition
      {
        Source = "mydata",
        Target = "/data",
        Type = VolumeType.Volume
      });
      svc.Volumes.Add(new LongServiceVolumeDefinition
      {
        Source = "/host/path",
        Target = "/container/path",
        Type = VolumeType.Bind,
        IsReadOnly = true
      });

      var shortVols = new List<ShortServiceVolumeDefinition>();
      var longVols = new List<LongServiceVolumeDefinition>();

      foreach (var vol in svc.Volumes)
      {
        if (vol is ShortServiceVolumeDefinition shortVol)
          shortVols.Add(shortVol);
        else if (vol is LongServiceVolumeDefinition longVol)
          longVols.Add(longVol);
      }

      Assert.Equal(2, shortVols.Count);
      Assert.Equal(2, longVols.Count);

      Assert.Equal("/var/lib/mysql", shortVols[0].Entry);
      Assert.Equal("./cache:/tmp/cache", shortVols[1].Entry);

      Assert.Equal("mydata", longVols[0].Source);
      Assert.Equal(VolumeType.Volume, longVols[0].Type);
      Assert.False(longVols[0].IsReadOnly);

      Assert.Equal("/host/path", longVols[1].Source);
      Assert.Equal(VolumeType.Bind, longVols[1].Type);
      Assert.True(longVols[1].IsReadOnly);
    }

    [Fact]
    public void ServiceVolumes_AllShort_VariousFormats()
    {
      var svc = new ComposeServiceDefinition();

      string[] entries =
      {
        "/var/lib/mysql",
        "/opt/data:/var/lib/mysql",
        "./cache:/tmp/cache",
        "~/configs:/etc/configs/:ro",
        "datavolume:/var/lib/mysql"
      };

      foreach (var e in entries)
        svc.Volumes.Add(new ShortServiceVolumeDefinition { Entry = e });

      Assert.Equal(5, svc.Volumes.Count);

      for (var i = 0; i < entries.Length; i++)
      {
        var shortVol = Assert.IsType<ShortServiceVolumeDefinition>(
          svc.Volumes[i]);
        Assert.Equal(entries[i], shortVol.Entry);
      }
    }

    [Fact]
    public void ServiceVolumes_AllLong_MultiType()
    {
      var svc = new ComposeServiceDefinition();

      svc.Volumes.Add(new LongServiceVolumeDefinition
      {
        Source = "dbdata",
        Target = "/var/lib/db",
        Type = VolumeType.Volume
      });
      svc.Volumes.Add(new LongServiceVolumeDefinition
      {
        Source = "/host/cache",
        Target = "/cache",
        Type = VolumeType.Bind
      });
      svc.Volumes.Add(new LongServiceVolumeDefinition
      {
        Target = "/tmp",
        Type = VolumeType.TmpFs
      });

      Assert.Equal(3, svc.Volumes.Count);

      var vol0 = Assert.IsType<LongServiceVolumeDefinition>(svc.Volumes[0]);
      Assert.Equal(VolumeType.Volume, vol0.Type);

      var vol1 = Assert.IsType<LongServiceVolumeDefinition>(svc.Volumes[1]);
      Assert.Equal(VolumeType.Bind, vol1.Type);

      var vol2 = Assert.IsType<LongServiceVolumeDefinition>(svc.Volumes[2]);
      Assert.Equal(VolumeType.TmpFs, vol2.Type);
      Assert.Null(vol2.Source);
    }

    #endregion

    #region VolumeType Enum Tests

    [Fact]
    public void VolumeType_DefaultForLongDefinition_IsVolume()
    {
      var vol = new LongServiceVolumeDefinition();
      Assert.Equal(VolumeType.Volume, vol.Type);
    }

    [Fact]
    public void VolumeType_AllValues_CanBeRoundTripped()
    {
      var vol = new LongServiceVolumeDefinition { Type = VolumeType.Volume };
      Assert.Equal(VolumeType.Volume, vol.Type);

      vol.Type = VolumeType.Bind;
      Assert.Equal(VolumeType.Bind, vol.Type);

      vol.Type = VolumeType.TmpFs;
      Assert.Equal(VolumeType.TmpFs, vol.Type);
    }

    #endregion

    #region LongServiceVolumeDefinition Options Tests

    [Fact]
    public void LongServiceVolumeDefinition_Options_MultipleEntries()
    {
      var vol = new LongServiceVolumeDefinition
      {
        Source = "mydata",
        Target = "/data",
        Type = VolumeType.Bind
      };
      vol.Options["propagation"] = "rprivate";
      vol.Options["consistency"] = "cached";

      Assert.Equal(2, vol.Options.Count);
      Assert.Equal("rprivate", vol.Options["propagation"]);
      Assert.Equal("cached", vol.Options["consistency"]);
    }

    [Fact]
    public void LongServiceVolumeDefinition_Options_CanBeOverwritten()
    {
      var vol = new LongServiceVolumeDefinition();
      vol.Options["nocopy"] = "false";
      vol.Options["nocopy"] = "true";

      Assert.Single(vol.Options);
      Assert.Equal("true", vol.Options["nocopy"]);
    }

    [Fact]
    public void LongServiceVolumeDefinition_Options_CanBeReplaced()
    {
      var vol = new LongServiceVolumeDefinition();
      vol.Options["key1"] = "val1";

      vol.Options = new Dictionary<string, string>
      {
        ["key2"] = "val2",
        ["key3"] = "val3"
      };

      Assert.Equal(2, vol.Options.Count);
      Assert.False(vol.Options.ContainsKey("key1"));
      Assert.Equal("val2", vol.Options["key2"]);
    }

    #endregion

    #region ComposeVolumeDefinition Tests (top-level volume)

    [Fact]
    public void ComposeVolumeDefinition_MultipleInstances_IndependentNames()
    {
      var vol1 = new ComposeVolumeDefinition { Name = "db-data" };
      var vol2 = new ComposeVolumeDefinition { Name = "cache-data" };

      Assert.NotEqual(vol1.Name, vol2.Name);
      Assert.Equal("db-data", vol1.Name);
      Assert.Equal("cache-data", vol2.Name);
    }

    #endregion
  }
}
