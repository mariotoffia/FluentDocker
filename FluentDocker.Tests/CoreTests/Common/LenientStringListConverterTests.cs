using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentDocker.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  [Trait("Category", "Unit")]
  public class LenientStringListConverterTests
  {
    [Fact]
    public void Read_Array_PreservesEmbeddedCommas()
    {
      var dto = JsonSerializer.Deserialize<ArgsDto>("""{"Args":["--label=a,b","--flag"]}""");

      Assert.NotNull(dto);
      Assert.Equal(["--label=a,b", "--flag"], dto.Args);
    }

    [Fact]
    public void Read_StringWithEmbeddedComma_DoesNotSplitComma()
    {
      var dto = JsonSerializer.Deserialize<ArgsDto>("""{"Args":"--label=a,b"}""");

      Assert.NotNull(dto);
      Assert.Equal(["--label=a,b"], dto.Args);
    }

    [Fact]
    public void Read_DockerServicePorts_SplitsCommaSpaceWithoutTrailingComma()
    {
      var dto = JsonSerializer.Deserialize<ArgsDto>("""{"Args":"*:30080->80/tcp, *:30443->443/tcp"}""");

      Assert.NotNull(dto);
      Assert.Equal(["*:30080->80/tcp", "*:30443->443/tcp"], dto.Args);
    }

    [Fact]
    public void Read_DockerServicePortRange_PreservesEmbeddedCommas()
    {
      var dto = JsonSerializer.Deserialize<ArgsDto>("""{"Args":"*:80-81,84,86-87->80"}""");

      Assert.NotNull(dto);
      Assert.Equal(["*:80-81,84,86-87->80"], dto.Args);
    }

    [Fact]
    public void Read_DockerServiceLsPorts_BareCommaJoin_SplitsEntries()
    {
      // ML-3: common Docker versions join `docker service ls` ports with a BARE comma —
      // multi-port services must not collapse into one bogus entry.
      var dto = JsonSerializer.Deserialize<ArgsDto>("""{"Args":"80/tcp,443/tcp"}""");

      Assert.NotNull(dto);
      Assert.Equal(["80/tcp", "443/tcp"], dto.Args);
    }

    [Fact]
    public void Read_DockerServiceLsPublishedPorts_BareCommaJoin_SplitsEntries()
    {
      var dto = JsonSerializer.Deserialize<ArgsDto>("""{"Args":"*:30080->80/tcp,*:30443->443/tcp"}""");

      Assert.NotNull(dto);
      Assert.Equal(["*:30080->80/tcp", "*:30443->443/tcp"], dto.Args);
    }

    private sealed class ArgsDto
    {
      [JsonConverter(typeof(LenientStringListConverter))]
      public List<string> Args { get; set; } = [];
    }
  }
}
