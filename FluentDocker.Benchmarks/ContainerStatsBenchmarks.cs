using BenchmarkDotNet.Attributes;
using FluentDocker.Drivers.Docker.Cli.Components;

namespace FluentDocker.Benchmarks
{
    /// <summary>
    /// Benchmarks for container stats parsing operations.
    /// These measure the performance of parsing Docker stats output.
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

        private DockerCliContainerDriver _driver = null!;

        [GlobalSetup]
        public void Setup()
        {
            _driver = new DockerCliContainerDriver(null);
        }

        [Benchmark(Description = "Parse simple stats JSON")]
        public void ParseSimpleStats()
        {
            // Use reflection to call the private method for benchmarking
            var method = typeof(DockerCliContainerDriver).GetMethod(
                "ParseStatsOutput",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            method?.Invoke(null, new object[] { SampleStatsJson });
        }

        [Benchmark(Description = "Parse complex stats JSON")]
        public void ParseComplexStats()
        {
            var method = typeof(DockerCliContainerDriver).GetMethod(
                "ParseStatsOutput",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            method?.Invoke(null, new object[] { ComplexStatsJson });
        }

        [Benchmark(Description = "Parse byte value - bytes")]
        public void ParseByteValue_Bytes()
        {
            var method = typeof(DockerCliContainerDriver).GetMethod(
                "ParseByteValue",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            method?.Invoke(null, new object[] { "1234B" });
        }

        [Benchmark(Description = "Parse byte value - MiB")]
        public void ParseByteValue_MiB()
        {
            var method = typeof(DockerCliContainerDriver).GetMethod(
                "ParseByteValue",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            method?.Invoke(null, new object[] { "256.5MiB" });
        }

        [Benchmark(Description = "Parse byte value - GiB")]
        public void ParseByteValue_GiB()
        {
            var method = typeof(DockerCliContainerDriver).GetMethod(
                "ParseByteValue",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            method?.Invoke(null, new object[] { "7.8GiB" });
        }

        [Benchmark(Description = "Parse percentage")]
        public void ParsePercent()
        {
            var method = typeof(DockerCliContainerDriver).GetMethod(
                "ParsePercent",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            method?.Invoke(null, new object[] { "45.78%" });
        }

        [Benchmark(Description = "Parse memory usage")]
        public void ParseMemoryUsage()
        {
            var method = typeof(DockerCliContainerDriver).GetMethod(
                "ParseMemoryUsage",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            method?.Invoke(null, new object[] { "256.5MiB / 2GiB" });
        }
    }
}
