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
    private const string SimpleCpuPercent = "0.15%";
    private const string SimpleMemoryPercent = "1.02%";
    private const string SimpleMemoryUsage = "15.5MiB / 1.5GiB";
    private const string SimpleNetIo = "1.2kB / 3.4MB";
    private const string SimpleBlockIo = "0B / 12.5MiB";
    private const string ComplexCpuPercent = "45.78%";
    private const string ComplexMemoryPercent = "29.38%";
    private const string ComplexMemoryUsage = "2.35GiB / 8GiB";
    private const string ComplexNetIo = "15.7MB / 892.3GB";
    private const string ComplexBlockIo = "1.2TB / 456.7GiB";

    [Benchmark(Description = "Parse simple stats via CliOutputParser")]
    public void ParseSimpleStats()
    {
      CliOutputParser.ParsePercent(SimpleCpuPercent);
      CliOutputParser.ParsePercent(SimpleMemoryPercent);
      CliOutputParser.ParseMemoryUsage(SimpleMemoryUsage);
      CliOutputParser.ParseIOPair(SimpleNetIo);
      CliOutputParser.ParseIOPair(SimpleBlockIo);
    }

    [Benchmark(Description = "Parse complex stats via CliOutputParser")]
    public void ParseComplexStats()
    {
      CliOutputParser.ParsePercent(ComplexCpuPercent);
      CliOutputParser.ParsePercent(ComplexMemoryPercent);
      CliOutputParser.ParseMemoryUsage(ComplexMemoryUsage);
      CliOutputParser.ParseIOPair(ComplexNetIo);
      CliOutputParser.ParseIOPair(ComplexBlockIo);
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
