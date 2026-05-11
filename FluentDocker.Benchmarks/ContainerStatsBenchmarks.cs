using BenchmarkDotNet.Attributes;
using FluentDocker.Common;

namespace FluentDocker.Benchmarks
{
  /// <summary>
  /// Benchmarks for container stats parsing operations.
  /// Uses the public <see cref="CliOutputParser"/> API directly instead of
  /// reflection against private methods.
  /// </summary>
  [MemoryDiagnoser]
  public class ContainerStatsBenchmarks
  {
    private const string SampleStatsJson = @"{
            ""Container"": ""abc123def456"",
            ""Name"": ""nginx-test"",
            ""CPUPerc"": ""0.15%"",
            ""MemUsage"": ""15.5MiB / 1.5GiB"",
            ""MemPerc"": ""1.02%"",
            ""NetIO"": ""1.2kB / 3.4MB"",
            ""BlockIO"": ""0B / 12.5MiB"",
            ""PIDs"": ""3""
        }";

    private System.Text.Json.JsonElement _simpleRoot;
    private System.Text.Json.JsonElement _complexRoot;

    [GlobalSetup]
    public void Setup()
    {
      _simpleRoot = System.Text.Json.JsonDocument.Parse(SampleStatsJson.Trim()).RootElement;
      _complexRoot = System.Text.Json.JsonDocument.Parse(ComplexStatsJson.Trim()).RootElement;
    }

    private const string ComplexStatsJson = @"{
            ""Container"": ""abc123def456789012345678901234567890123456789012345678901234"",
            ""Name"": ""complex-service-with-long-name"",
            ""CPUPerc"": ""45.78%"",
            ""MemUsage"": ""2.35GiB / 8GiB"",
            ""MemPerc"": ""29.38%"",
            ""NetIO"": ""15.7MB / 892.3GB"",
            ""BlockIO"": ""1.2TB / 456.7GiB"",
            ""PIDs"": ""256""
        }";

    [Benchmark(Description = "Parse simple stats via CliOutputParser")]
    public void ParseSimpleStats()
    {
      CliOutputParser.ParsePercent(_simpleRoot.GetProperty("CPUPerc").GetString());
      CliOutputParser.ParsePercent(_simpleRoot.GetProperty("MemPerc").GetString());
      CliOutputParser.ParseMemoryUsage(_simpleRoot.GetProperty("MemUsage").GetString());
      CliOutputParser.ParseIOPair(_simpleRoot.GetProperty("NetIO").GetString());
      CliOutputParser.ParseIOPair(_simpleRoot.GetProperty("BlockIO").GetString());
    }

    [Benchmark(Description = "Parse complex stats via CliOutputParser")]
    public void ParseComplexStats()
    {
      CliOutputParser.ParsePercent(_complexRoot.GetProperty("CPUPerc").GetString());
      CliOutputParser.ParsePercent(_complexRoot.GetProperty("MemPerc").GetString());
      CliOutputParser.ParseMemoryUsage(_complexRoot.GetProperty("MemUsage").GetString());
      CliOutputParser.ParseIOPair(_complexRoot.GetProperty("NetIO").GetString());
      CliOutputParser.ParseIOPair(_complexRoot.GetProperty("BlockIO").GetString());
    }

    [Benchmark(Description = "Parse byte value - bytes")]
    public long ParseByteValue_Bytes() => CliOutputParser.ParseByteValue("1234B");

    [Benchmark(Description = "Parse byte value - MiB")]
    public long ParseByteValue_MiB() => CliOutputParser.ParseByteValue("256.5MiB");

    [Benchmark(Description = "Parse byte value - GiB")]
    public long ParseByteValue_GiB() => CliOutputParser.ParseByteValue("7.8GiB");

    [Benchmark(Description = "Parse percentage")]
    public double ParsePercent() => CliOutputParser.ParsePercent("45.78%");

    [Benchmark(Description = "Parse memory usage")]
    public (long, long) ParseMemoryUsage() => CliOutputParser.ParseMemoryUsage("256.5MiB / 2GiB");
  }
}
