using System;

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
    private readonly string _query;

    private ModelRunnerEndpoint(Uri baseAddress, string engine, string unixSocketPath, bool includeEngineInPath, string basePath = null, string query = null)
    {
      BaseAddress = baseAddress;
      Engine = engine;
      UnixSocketPath = unixSocketPath;
      IncludeEngineInPath = includeEngineInPath;
      _basePath = basePath;
      _query = query;
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
    public string EngineV1Path(string suffix)
    {
      var path = _basePath != null ? _basePath + suffix : EnginePath + "/v1" + suffix;
      return _query == null ? path : path + _query;
    }

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
        new(BaseAddress, Engine, UnixSocketPath, include, _basePath, _query);

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
      ArgumentNullException.ThrowIfNull(url);
      if (!IsSupportedUrl(url))
        throw new ArgumentException("Model runner endpoint URL must be an absolute http(s) URL with a host.", nameof(url));

      var authority = new Uri(url.GetLeftPart(UriPartial.Authority));
      var path = url.AbsolutePath.TrimEnd('/');
      var basePath = string.IsNullOrEmpty(path) ? null : path;
      var query = string.IsNullOrEmpty(url.Query) ? null : url.Query;
      return new ModelRunnerEndpoint(authority, engine, null, true, basePath, query);
    }

    /// <summary>Creates a host-TCP endpoint (<c>http://localhost:port</c>).</summary>
    /// <param name="port">The TCP port (default 12434).</param>
    /// <param name="engine">The engine name (default <c>llama.cpp</c>).</param>
    /// <returns>A host-TCP endpoint.</returns>
    public static ModelRunnerEndpoint HostTcp(int port = DefaultPort, string engine = DefaultEngine) =>
        new(new Uri($"http://localhost:{ValidatePort(port)}"), engine, null, true);

    /// <summary>Creates a container-internal endpoint (<c>http://model-runner.docker.internal:port</c>).</summary>
    /// <param name="port">The TCP port (default 12434).</param>
    /// <param name="engine">The engine name (default <c>llama.cpp</c>).</param>
    /// <returns>A container-internal endpoint.</returns>
    public static ModelRunnerEndpoint ContainerInternal(int port = DefaultPort, string engine = DefaultEngine) =>
        new(new Uri($"http://model-runner.docker.internal:{ValidatePort(port)}"), engine, null, true);

    /// <summary>Creates a unix-socket endpoint.</summary>
    /// <param name="socketPath">The explicit socket path.</param>
    /// <param name="engine">The engine name (default <c>llama.cpp</c>).</param>
    /// <returns>A unix-socket endpoint.</returns>
    /// <remarks>
    /// Preview caveat: the socket form assumes the runner serves <c>/engines/…</c> directly
    /// on the supplied socket (request paths are built as <c>/engines/{engine}/v1/…</c> with
    /// no extra prefix). Docker Desktop host-socket routing prefixes are not guessed here;
    /// pass a socket path only after confirming that route for your platform.
    /// </remarks>
    public static ModelRunnerEndpoint UnixSocket(string socketPath, string engine = DefaultEngine)
    {
      if (string.IsNullOrWhiteSpace(socketPath))
        throw new ArgumentException("Unix socket endpoint requires an explicit socket path.", nameof(socketPath));
      return new ModelRunnerEndpoint(new Uri("http://localhost"), engine, socketPath, true);
    }

    /// <summary>Creates an endpoint targeting an arbitrary base address.</summary>
    /// <param name="baseAddress">The base address.</param>
    /// <param name="engine">The engine name (default <c>llama.cpp</c>).</param>
    /// <returns>A custom endpoint.</returns>
    /// <remarks>
    /// When <paramref name="baseAddress"/> carries a non-root path (anything other than an
    /// empty path or <c>"/"</c>, e.g. <c>https://host:9000/v1</c>), that path is PRESERVED and
    /// request paths are appended to it directly — i.e. the call behaves exactly like
    /// <see cref="Raw(Uri, string)"/>, with no <c>/engines/{engine}/v1</c> prefix added. An
    /// authority-only base (no path, or just <c>"/"</c>) keeps the documented behavior of
    /// appending <c>/engines/{engine}/v1/…</c>.
    /// </remarks>
    public static ModelRunnerEndpoint Custom(Uri baseAddress, string engine = DefaultEngine)
    {
      ArgumentNullException.ThrowIfNull(baseAddress);
      if (!IsSupportedUrl(baseAddress))
        throw new ArgumentException("Model runner endpoint URL must be an absolute http(s) URL with a host.", nameof(baseAddress));

      // A non-root path (e.g. https://host:9000/v1) is a fully-formed base the caller wants
      // honored verbatim — delegate to Raw so the path is preserved instead of discarded and
      // re-prefixed with /engines/{engine}/v1. An authority-only Uri (path "" or "/") keeps
      // the engine-prefix behavior below.
      var path = baseAddress.AbsolutePath;
      if (!string.IsNullOrEmpty(path) && path != "/")
        return Raw(baseAddress, engine);

      var query = string.IsNullOrEmpty(baseAddress.Query) ? null : baseAddress.Query;
      return new ModelRunnerEndpoint(baseAddress, engine, null, true, query: query);
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

    private static int ValidatePort(int port)
    {
      if (port is < 1 or > 65535)
        throw new ArgumentOutOfRangeException(nameof(port), port, "TCP port must be in the range 1-65535.");
      return port;
    }

    /// <summary>
    /// Attempts to construct an endpoint from the <c>DOCKER_MODEL_RUNNER_URL</c>
    /// environment variable (the highest-priority resolution step).
    /// </summary>
    /// <param name="endpoint">The resolved endpoint, or <c>null</c> when the variable is unset.</param>
    /// <returns><c>true</c> when the variable is set to a valid absolute http(s) URL; <c>false</c>
    /// when it is unset or invalid.</returns>
    public static bool TryFromEnvironment(out ModelRunnerEndpoint endpoint)
    {
      var value = Environment.GetEnvironmentVariable(UrlEnvironmentVariable);
      if (string.IsNullOrWhiteSpace(value))
      {
        // Unset (or whitespace) is the intended "use the default" signal — fall back quietly.
        endpoint = null;
        return false;
      }

      if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !IsSupportedUrl(uri))
      {
        endpoint = null;
        return false;
      }

      // Raw() preserves a path-bearing URL (e.g. an injected
      // http://host:12434/engines/v1) instead of discarding it like Custom() would.
      endpoint = Raw(uri);
      return true;
    }

    /// <summary>
    /// Whether a URI is a usable model-runner endpoint: an absolute http(s) URL with a host.
    /// This is the single validation shared by <c>DOCKER_MODEL_RUNNER_URL</c> parsing here and
    /// the env/Compose <c>ModelRunnerEnvironment</c> Try* paths, so a scheme-less or non-http(s)
    /// value (e.g. <c>ftp://</c>, <c>file://</c>) is rejected identically by every <c>Try*</c> API
    /// rather than passing one and failing later.
    /// </summary>
    /// <param name="uri">The candidate URI (may be null).</param>
    /// <returns><c>true</c> when the URI is absolute, http(s), and has a non-empty host.</returns>
    public static bool IsSupportedUrl(Uri uri) =>
        uri is { IsAbsoluteUri: true } &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        !string.IsNullOrEmpty(uri.Host);

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
          && string.Equals(_query, other._query, StringComparison.Ordinal)
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
      hash.Add(_query, StringComparer.Ordinal);
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
