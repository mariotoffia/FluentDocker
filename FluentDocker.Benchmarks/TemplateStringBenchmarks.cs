using BenchmarkDotNet.Attributes;
using FluentDocker.Model.Common;

namespace FluentDocker.Benchmarks
{
  /// <summary>
  /// Benchmarks for TemplateString operations.
  /// These measure path template interpolation performance.
  /// </summary>
  [MemoryDiagnoser]
  public class TemplateStringBenchmarks
  {
    private TemplateString _simplePath = null!;
    private TemplateString _tempPath = null!;
    private TemplateString _randomPath = null!;
    private TemplateString _envPath = null!;
    private TemplateString _complexPath = null!;

    [GlobalSetup]
    public void Setup()
    {
      _simplePath = new TemplateString("/some/static/path/file.txt");
      _tempPath = new TemplateString("${TEMP}/my-temp-file.txt");
      _randomPath = new TemplateString("/path/${RND}/file.txt");
      _envPath = new TemplateString("/path/${E_HOME}/config");
      _complexPath = new TemplateString("${TEMP}/${RND}/container-${E_USER:-default}/data");
    }

    [Benchmark(Description = "Resolve static path (no variables)")]
    public string ResolveStaticPath()
    {
      return _simplePath.Rendered;
    }

    [Benchmark(Description = "Resolve ${TEMP} path")]
    public string ResolveTempPath()
    {
      return _tempPath.Rendered;
    }

    [Benchmark(Description = "Resolve ${RND} path")]
    public string ResolveRandomPath()
    {
      // Note: Each call generates a new random value
      var ts = new TemplateString("/path/${RND}/file.txt");
      return ts.Rendered;
    }

    [Benchmark(Description = "Resolve ${E_*} environment path")]
    public string ResolveEnvPath()
    {
      return _envPath.Rendered;
    }

    [Benchmark(Description = "Resolve complex path with multiple variables")]
    public string ResolveComplexPath()
    {
      var ts = new TemplateString("${TEMP}/${RND}/container-${E_USER:-default}/data");
      return ts.Rendered;
    }

    [Benchmark(Description = "Create new TemplateString (simple)")]
    public TemplateString CreateSimpleTemplateString()
    {
      return new TemplateString("/some/static/path");
    }

    [Benchmark(Description = "Create new TemplateString (with variables)")]
    public TemplateString CreateVariableTemplateString()
    {
      return new TemplateString("${TEMP}/${RND}/file.txt");
    }

    [Benchmark(Description = "Implicit string to TemplateString conversion")]
    public TemplateString ImplicitConversion()
    {
      TemplateString ts = "/some/path/${TEMP}/file.txt";
      return ts;
    }

    [Benchmark(Description = "TemplateString ToString")]
    public string TemplateStringToString()
    {
      return _complexPath.ToString();
    }
  }
}
