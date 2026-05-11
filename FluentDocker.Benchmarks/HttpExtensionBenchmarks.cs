using System.Net;
using BenchmarkDotNet.Attributes;

namespace FluentDocker.Benchmarks
{
  /// <summary>
  /// Benchmarks for HTTP extension operations.
  /// Note: Some benchmarks are simulated since they can't make real network calls in benchmarks.
  /// </summary>
  [MemoryDiagnoser]
  public class HttpExtensionBenchmarks
  {
    private const string SampleUrl = "http://localhost:12345/test";
    private const string SampleUrlWithPath = "http://localhost:12345/api/v1/container/stats";

    [Benchmark(Description = "Parse URL string")]
    public Uri ParseUrl()
    {
      return new Uri(SampleUrl);
    }

    [Benchmark(Description = "Parse complex URL string")]
    public Uri ParseComplexUrl()
    {
      return new Uri(SampleUrlWithPath);
    }

    [Benchmark(Description = "Build request URL with parameters")]
    public string BuildRequestUrl()
    {
      var baseUrl = "http://localhost:12345/api/v1";
      var endpoint = "/containers/abc123/stats";
      var parameters = "?stream=false&one-shot=true";
      return $"{baseUrl}{endpoint}{parameters}";
    }

    [Benchmark(Description = "URL validation check")]
    public bool ValidateUrl()
    {
      return Uri.TryCreate(SampleUrl, UriKind.Absolute, out var uri) &&
             (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    [Benchmark(Description = "Null/empty URL check pattern")]
    public bool CheckNullOrEmptyUrl()
    {
      var url = SampleUrl;
      return !string.IsNullOrWhiteSpace(url);
    }

    [Benchmark(Description = "HttpStatusCode OK creation")]
    public HttpStatusCode CreateSuccessStatusCode()
    {
      return HttpStatusCode.OK;
    }

    [Benchmark(Description = "HttpStatusCode error creation")]
    public HttpStatusCode CreateErrorStatusCode()
    {
      return HttpStatusCode.InternalServerError;
    }

    [Benchmark(Description = "URL string concatenation")]
    public string ConcatenateUrlParts()
    {
      return string.Concat("http://", "localhost", ":", "12345", "/api/v1/test");
    }

    [Benchmark(Description = "URL interpolation")]
    public string InterpolateUrl()
    {
      var host = "localhost";
      var port = 12345;
      var path = "/api/v1/test";
      return $"http://{host}:{port}{path}";
    }
  }
}
