using System.Collections.Generic;
using FluentDocker.Common;
using FluentDocker.Model.Containers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  /// <summary>
  /// MODEL-2: non-nullable int/long DTO fields must tolerate daemon drift (JSON null or a
  /// fractional number) the way the globally-tolerant bools/dates already do, instead of failing
  /// the whole inspect object. <see cref="JsonHelper.ApplyLenientIntConverters"/> wires
  /// <see cref="LenientInt32Converter"/>/<see cref="LenientInt64Converter"/> onto every non-nullable
  /// int/long property of FluentDocker.Model DTOs. Shares a non-parallel collection with the
  /// COMMON-3 drift-count tests so the deliberate drift here cannot race their negative assertions.
  /// </summary>
  [Trait("Category", "Unit")]
  [Collection("LenientDriftCounter")]
  public sealed class LenientIntConverterModelTests
  {
    [Fact]
    public void TryDeserialize_ContainerWithNullPid_PreservesContainerAndDefaultsPid()
    {
      var json = """
          [{
            "Id": "abc123",
            "State": {
              "Pid": null
            }
          }]
          """;

      var ok = JsonHelper.TryDeserialize<List<Container>>(json, out var containers);

      Assert.True(ok);
      var container = Assert.Single(containers!);
      Assert.Equal("abc123", container.Id);
      Assert.NotNull(container.State);
      Assert.Equal(0, container.State!.Pid);
    }

    [Fact]
    public void TryDeserialize_ContainerWithFractionalPid_PreservesContainerAndTruncates()
    {
      var json = """
          [{
            "Id": "abc123",
            "State": {
              "Pid": 4321.9
            }
          }]
          """;

      var ok = JsonHelper.TryDeserialize<List<Container>>(json, out var containers);

      Assert.True(ok);
      var container = Assert.Single(containers!);
      Assert.Equal("abc123", container.Id);
      Assert.Equal(4321, container.State!.Pid);
    }

    [Fact]
    public void TryDeserialize_ContainerWithFractionalExitCode_PreservesContainerAndCoversLong()
    {
      // ExitCode is a non-nullable long; a fractional daemon value must be tolerated (long coverage).
      var json = """
          [{
            "Id": "abc123",
            "State": {
              "ExitCode": 137.0
            }
          }]
          """;

      var ok = JsonHelper.TryDeserialize<List<Container>>(json, out var containers);

      Assert.True(ok);
      var container = Assert.Single(containers!);
      Assert.Equal("abc123", container.Id);
      Assert.Equal(137L, container.State!.ExitCode);
    }
  }
}
