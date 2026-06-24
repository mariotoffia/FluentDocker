using System;
using System.IO;

namespace FluentDocker.Model.Models
{
  /// <summary>
  /// An immutable description of where (and how) to reach a model runner's
  /// OpenAI-compatible inference surface: the base address, the engine path
  /// segment, an optional unix socket and whether the engine name is included in
  /// the request path (<c>/engines/llama.cpp/v1</c> vs <c>/engines/v1</c>).
  /// </summary>
  public sealed class ModelRunnerEndpoint : IEquatable<ModelRunnerEndpoint>
  {
    /// <summary>The environment variable that overrides endpoint resolution.</summary>
    public const string UrlEnvironmentVariable = "DOCKER_MODEL_RUNNER_URL";

    private const int DefaultPort = 12434;
    private const string DefaultEngine = "llama.cpp";

    private readonly string _basePath;

    private ModelRunnerEndpoint(Uri baseAddress, string engine, string unixSocketPath, bool includeEngineInPath, string basePath = null)
    {
      BaseAddress = baseAddress;
      Engine = engine;
      UnixSocketPath = unixSocketPath;
      IncludeEngineInPath = includeEngineInPath;
      _basePath = basePath;
    }

    /// <summary>The base address (host root, e.g. <c>http://localhost:12434</c>).</summary>
    public Uri BaseAddress { get; }

    /// <summary>The engine path segment (e.g. <c>llama.cpp</c>).</summary>
    public string Engine { get; }

    /// <summary>When non-null, the connection is made over this unix socket.</summary>
    public string UnixSocketPath { get; }

    /// <summary>When true, the engine name is included in the request path.</summary>
    public bool IncludeEngineInPath { get; }

    /// <summary>
    /// The engine path prefix, e.g. <c>/engines/llama.cpp</c> or <c>/engines</c>.
    /// When a raw base path was supplied (a fully-formed <c>…/v1</c> base, e.g. an
    /// injected <c>LLM_URL</c>), that path is returned verbatim.
    /// </summary>
    public string EnginePath => _basePath ?? (IncludeEngineInPath ? "/engines/" + Engine : "/engines");

    /// <summary>
    /// Builds an engine <c>v1</c> path, e.g. <c>EngineV1Path("/chat/completions")</c>
    /// → <c>/engines/llama.cpp/v1/chat/completions</c>. When a raw base path is set,
    /// the suffix is appended to it directly (the base already includes <c>/v1</c>).
    /// </summary>
    /// <param name="suffix">The path suffix, beginning with <c>/</c>.</param>
    /// <returns>The composed request path.</returns>
    public string EngineV1Path(string suffix) => _basePath != null ? _basePath + suffix : EnginePath + "/v1" + suffix;

    /// <summary>
    /// Resolves a full absolute URI from <see cref="BaseAddress"/> and the engine path.
    /// </summary>
    /// <param name="suffix">The path suffix, beginning with <c>/</c>.</param>
    /// <returns>The absolute request URI.</returns>
    public Uri ResolveUri(string suffix) => new(BaseAddress, EngineV1Path(suffix));

    /// <summary>Returns a copy with the engine-in-path flag toggled.</summary>
    /// <param name="include">Whether to include the engine name in the path.</param>
    /// <returns>A new endpoint.</returns>
    public ModelRunnerEndpoint WithEngineInPath(bool include) =>
        new(BaseAddress, Engine, UnixSocketPath, include, _basePath);

    /// <summary>
    /// Creates an endpoint from a fully-formed base URL (authority + an optional
    /// <c>…/v1</c> path, e.g. an injected <c>LLM_URL</c> like
    /// <c>http://model-runner.docker.internal:12434/engines/v1</c>). Request paths are
    /// appended to the URL's path directly (no engine prefix is added).
    /// </summary>
    /// <param name="url">The full base URL.</param>
    /// <param name="engine">The engine name (informational).</param>
    /// <returns>A raw-base-path endpoint.</returns>
    public static ModelRunnerEndpoint Raw(Uri url, string engine = DefaultEngine)
    {
      if (url == null)
        throw new ArgumentNullException(nameof(url));

      var authority = new Uri(url.GetLeftPart(UriPartial.Authority));
      var path = url.AbsolutePath.TrimEnd('/');
      var basePath = string.IsNullOrEmpty(path) ? null : path;
      return new ModelRunnerEndpoint(authority, engine, null, true, basePath);
    }

    /// <summary>Creates a host-TCP endpoint (<c>http://localhost:port</c>).</summary>
    /// <param name="port">The TCP port (default 12434).</param>
    /// <param name="engine">The engine name (default <c>llama.cpp</c>).</param>
    /// <returns>A host-TCP endpoint.</returns>
    public static ModelRunnerEndpoint HostTcp(int port = DefaultPort, string engine = DefaultEngine) =>
        new(new Uri($"http://localhost:{port}"), engine, null, true);

    /// <summary>Creates a container-internal endpoint (<c>http://model-runner.docker.internal:port</c>).</summary>
    /// <param name="port">The TCP port (default 12434).</param>
    /// <param name="engine">The engine name (default <c>llama.cpp</c>).</param>
    /// <returns>A container-internal endpoint.</returns>
    public static ModelRunnerEndpoint ContainerInternal(int port = DefaultPort, string engine = DefaultEngine) =>
        new(new Uri($"http://model-runner.docker.internal:{port}"), engine, null, true);

    /// <summary>Creates a unix-socket endpoint.</summary>
    /// <param name="socketPath">The socket path; defaults to <c>$HOME/.docker/run/docker.sock</c>.</param>
    /// <param name="engine">The engine name (default <c>llama.cpp</c>).</param>
    /// <returns>A unix-socket endpoint.</returns>
    public static ModelRunnerEndpoint UnixSocket(string socketPath = null, string engine = DefaultEngine)
    {
      var path = socketPath ?? DefaultSocketPath();
      return new ModelRunnerEndpoint(new Uri("http://localhost"), engine, path, true);
    }

    /// <summary>Creates an endpoint targeting an arbitrary base address.</summary>
    /// <param name="baseAddress">The base address.</param>
    /// <param name="engine">The engine name (default <c>llama.cpp</c>).</param>
    /// <returns>A custom endpoint.</returns>
    public static ModelRunnerEndpoint Custom(Uri baseAddress, string engine = DefaultEngine)
    {
      if (baseAddress == null)
        throw new ArgumentNullException(nameof(baseAddress));

      return new ModelRunnerEndpoint(baseAddress, engine, null, true);
    }

    /// <summary>
    /// The default endpoint, following the documented resolution order: the
    /// <c>DOCKER_MODEL_RUNNER_URL</c> environment variable when set, otherwise host
    /// TCP on port 12434. This is the single source of truth for "where is the
    /// runner when the caller did not say".
    /// </summary>
    /// <returns>The default endpoint.</returns>
    public static ModelRunnerEndpoint Default() =>
        TryFromEnvironment(out var endpoint) ? endpoint : HostTcp();

    /// <summary>
    /// Attempts to construct an endpoint from the <c>DOCKER_MODEL_RUNNER_URL</c>
    /// environment variable (the highest-priority resolution step).
    /// </summary>
    /// <param name="endpoint">The resolved endpoint, or <c>null</c>.</param>
    /// <returns><c>true</c> when the variable is set to a valid absolute URI.</returns>
    public static bool TryFromEnvironment(out ModelRunnerEndpoint endpoint)
    {
      var value = Environment.GetEnvironmentVariable(UrlEnvironmentVariable);
      if (!string.IsNullOrWhiteSpace(value) && Uri.TryCreate(value, UriKind.Absolute, out var uri))
      {
        // Raw() preserves a path-bearing URL (e.g. an injected
        // http://host:12434/engines/v1) instead of discarding it like Custom() would.
        endpoint = Raw(uri);
        return true;
      }

      endpoint = null;
      return false;
    }

    private static string DefaultSocketPath()
    {
      var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
      return Path.Combine(home, ".docker", "run", "docker.sock");
    }

    /// <inheritdoc />
    public bool Equals(ModelRunnerEndpoint other)
    {
      if (other is null)
        return false;
      if (ReferenceEquals(this, other))
        return true;

      return Equals(BaseAddress, other.BaseAddress)
          && string.Equals(Engine, other.Engine, StringComparison.Ordinal)
          && string.Equals(UnixSocketPath, other.UnixSocketPath, StringComparison.Ordinal)
          && string.Equals(_basePath, other._basePath, StringComparison.Ordinal)
          && IncludeEngineInPath == other.IncludeEngineInPath;
    }

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as ModelRunnerEndpoint);

    /// <inheritdoc />
    public override int GetHashCode()
    {
      var hash = new HashCode();
      hash.Add(BaseAddress);
      hash.Add(Engine, StringComparer.Ordinal);
      hash.Add(UnixSocketPath, StringComparer.Ordinal);
      hash.Add(_basePath, StringComparer.Ordinal);
      hash.Add(IncludeEngineInPath);
      return hash.ToHashCode();
    }

    /// <summary>Value equality operator.</summary>
    public static bool operator ==(ModelRunnerEndpoint left, ModelRunnerEndpoint right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Value inequality operator.</summary>
    public static bool operator !=(ModelRunnerEndpoint left, ModelRunnerEndpoint right) => !(left == right);
  }
}
