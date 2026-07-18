#nullable enable
#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentDocker.Common;
using FluentDocker.Extensions;
using FluentDocker.Model.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Resources;
using Xunit;

namespace FluentDocker.Tests.CoreTests
{
  [Trait("Category", "Unit")]
  public sealed class CoreModelCommonExtensionsReadinessTests
  {
    // CA1869: one cached options instance for the LenientStringDictionaryConverter tests.
    private static readonly JsonSerializerOptions LenientDictionaryOptions = CreateLenientDictionaryOptions();

    private static JsonSerializerOptions CreateLenientDictionaryOptions()
    {
      var options = new JsonSerializerOptions();
      options.Converters.Add(new LenientStringDictionaryConverter());
      return options;
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void LenientStringDictionaryConverter_WhenRegisteredInOptions_ReadsObjectWithoutRecursion()
    {
      var options = LenientDictionaryOptions;

      var value = JsonSerializer.Deserialize<Dictionary<string, string>>("""{"a":"b"}""", options);

      Assert.NotNull(value);
      Assert.Equal("b", value["a"]);
      Assert.Equal("""{"a":"b"}""", JsonSerializer.Serialize(value, options));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void LenientStringListConverter_WhenRegisteredInOptions_ReadsArrayWithoutRecursion()
    {
      var options = new JsonSerializerOptions();
      options.Converters.Add(new LenientStringListConverter());

      var value = JsonSerializer.Deserialize<List<string>>("""["x","y"]""", options);

      Assert.Equal(["x", "y"], value);
      Assert.Equal("""["x","y"]""", JsonSerializer.Serialize(value, options));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void LenientStringDictionaryConverter_CompactValueWithCommas_KeepsSingleEntry()
    {
      var options = LenientDictionaryOptions;

      var value = JsonSerializer.Deserialize<Dictionary<string, string>>(
        "\"org.opencontainers.image.description=a,b,c\"",
        options);

      Assert.NotNull(value);
      var pair = Assert.Single(value);
      Assert.Equal("org.opencontainers.image.description", pair.Key);
      Assert.Equal("a,b,c", pair.Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void LenientStringDictionaryConverter_CompactValueWithTrailingComma_DropsDanglingSeparator()
    {
      var options = LenientDictionaryOptions;

      var value = JsonSerializer.Deserialize<Dictionary<string, string>>("\"a=1,\"", options);

      Assert.NotNull(value);
      var pair = Assert.Single(value);
      Assert.Equal("a", pair.Key);
      Assert.Equal("1", pair.Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void LenientStringDictionaryConverter_EmptyArray_ReturnsEmptyDictionary()
    {
      var options = LenientDictionaryOptions;

      var value = JsonSerializer.Deserialize<Dictionary<string, string>>("[]", options);

      Assert.NotNull(value);
      Assert.Empty(value);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("""{"Value":true}""", true)]
    [InlineData("""{"Value":false}""", false)]
    [InlineData("""{"Value":"1"}""", true)]
    [InlineData("""{"Value":"0"}""", false)]
    [InlineData("""{"Value":1}""", true)]
    [InlineData("""{"Value":0}""", false)]
    [InlineData("""{"Value":null}""", false)]
    public void LenientBoolConverter_ReadsDockerBooleanShapes(string json, bool expected)
    {
      var dto = JsonSerializer.Deserialize<BoolDto>(json);

      Assert.NotNull(dto);
      Assert.Equal(expected, dto.Value);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("""[{"Id":"abc","State":{"Status":"running","Running":null}}]""")]
    [InlineData("""[{"Id":"abc","State":{"Status":"running","Running":"true","OOMKilled":null}}]""")]
    [InlineData("""[{"Id":"abc","State":{"Status":"running","Running":1,"Dead":0}}]""")]
    public void ContainerInspect_DriftingStateBooleans_DoNotFailWholeDeserialization(string json)
    {
      // One drifting daemon-emitted boolean (null / "true" / 0/1) in one container must not
      // fail the entire inspect for all containers: every non-nullable bool on model DTOs
      // goes through LenientBoolConverter via the JsonHelper type-info modifier.
      var ok = JsonHelper.TryDeserialize<List<Container>>(json, out var containers, out var error);

      Assert.True(ok, error?.Message);
      Assert.NotNull(containers);
      var state = Assert.Single(containers!).State;
      Assert.NotNull(state);
      Assert.Equal("running", state!.Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ContainerInspect_NullMountRwAndConfigBooleans_ReadAsFalse()
    {
      var json = """
        {
          "Id": "abc",
          "Mounts": [{"Destination":"/data","RW":null}],
          "Config": {"Tty":null,"OpenStdin":"false"}
        }
        """;

      var ok = JsonHelper.TryDeserialize<Container>(json, out var container, out var error);

      Assert.True(ok, error?.Message);
      Assert.NotNull(container?.Mounts);
      Assert.False(container!.Mounts![0].RW);
      Assert.NotNull(container.Config);
      Assert.False(container.Config!.Tty);
      Assert.False(container.Config.OpenStdin);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShellArgParser_RoundTripsArgumentsQuotedByCommandLineQuoting()
    {
      var expected = new[]
      {
        "run",
        @"C:\temp\app.exe",
        "arg with spaces",
        "quote\"inside",
        @"C:\Program Files\App\"
      };
      var command = string.Join(" ", expected.Select(CommandLineQuoting.QuoteArgumentIfNeeded));

      var actual = ShellArgParser.Parse(command);

      Assert.Equal(expected, actual);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CommandResponseMap_PreservesSuccessfulExitCode()
    {
      var response = CommandResponse<string>.Ok("abc", "stdout", 3);

      var mapped = response.Map(x => x.Length);

      Assert.True(mapped.Success);
      Assert.Equal(3, mapped.ExitCode);
      Assert.Equal(3, mapped.Data);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ResourceExtensionsToFile_MissingResourceSegment_ThrowsClearFluentDockerException()
    {
      var uri = new EmbeddedUri("emb:FluentDocker.Tests/Some.Namespace");

      var ex = Assert.Throws<FluentDockerException>(() => uri.ToFile(".out/missing-resource-segment"));

      Assert.Contains(uri.ToString(), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ResourceQuery_IncludeOverlappingNamespaceBoundary_ReturnsNoMatchInsteadOfThrowing()
    {
      // CE-1: the requested name overlaps the query root ("Foo.Bar" + "Bar.child.txt" vs the
      // manifest resource "Foo.Bar.child.txt"): the suffix-match prefix falls SHORT of the
      // root and previously sliced below it (ArgumentOutOfRangeException). It must simply
      // not match.
      var assembly = typeof(CoreModelCommonExtensionsReadinessTests).Assembly.GetName().Name;

      var resources = new ResourceQuery()
        .From(assembly)
        .Namespace("Foo.Bar")
        .Include("Bar.child.txt")
        .ToArray();

      Assert.Empty(resources);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ResourceQuery_RecursiveNamespace_DoesNotMatchSiblingPrefix()
    {
      var assembly = typeof(CoreModelCommonExtensionsReadinessTests).Assembly.GetName().Name;

      var resources = new ResourceQuery()
        .From(assembly)
        .Namespace("Foo.Bar")
        .Query()
        .ToArray();

      Assert.Contains(resources, x => x.Namespace == "Foo.Bar" && x.Resource == "child.txt");
      Assert.DoesNotContain(resources, x => x.Namespace == "Foo.Barbaz");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BridgeNetwork_DeserializesStringEncodedIpPrefixLength()
    {
      var network = JsonSerializer.Deserialize<BridgeNetwork>("""{"IPPrefixLen":"24"}""");

      Assert.NotNull(network);
      Assert.Equal(24, network.IPPrefixLen);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Unit_ImplementsEquatableAndAllInstancesAreEqual()
    {
      var unit = Unit.Default;
      var other = new Unit();

      Assert.IsAssignableFrom<IEquatable<Unit>>(unit);
      Assert.True(unit.Equals(other));
      Assert.True(unit == other);
      Assert.False(unit != other);
      Assert.Equal(unit.GetHashCode(), other.GetHashCode());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void HostIpEndpoint_TryGetAccessors_ReturnFalseInsteadOfThrowingForPartialDto()
    {
      var endpoint = new HostIpEndpoint();

      Assert.False(endpoint.TryGetPort(out var port));
      Assert.Equal(0, port);
      Assert.True(endpoint.TryGetAddress(out var address));
      Assert.Equal(IPAddress.Any, address);

      endpoint.HostIp = "not-an-ip-address";

      Assert.False(endpoint.TryGetAddress(out address));
      Assert.Null(address);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("emb:MyAssembly/My.Namespace/file.txt", true)]
    [InlineData("emb:MyAssembly/My.Namespace", true)]
    [InlineData("emb:AssemblyOnly", false)]
    [InlineData("file:MyAssembly/My.Namespace/file.txt", false)]
    public void EmbeddedUriIsValid_UsesSameParsingRulesAsConstructor(string value, bool expected)
    {
      Assert.Equal(expected, EmbeddedUri.IsValid(value));
    }

    private sealed class BoolDto
    {
      [JsonConverter(typeof(LenientBoolConverter))]
      public bool Value { get; set; } = true;
    }
  }
}
