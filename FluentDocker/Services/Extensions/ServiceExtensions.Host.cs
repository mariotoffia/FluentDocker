using System;

namespace FluentDocker.Services.Extensions
{
  public static partial class ServiceExtensions
  {
    #region Host Extensions

    /// <summary>
    /// Gets the Docker host address.
    /// </summary>
    /// <param name="service">The host service.</param>
    /// <returns>The Docker host address.</returns>
    /// <remarks>
    /// Uses <see cref="IServiceAsync.Name"/> when it contains a URI such as
    /// <c>tcp://host:2376</c>; otherwise falls back to localhost.
    /// </remarks>
    public static string GetDockerHost(this IHostService service)
    {
      if (service.IsNative)
        return "127.0.0.1";

      var configured = TryGetDockerHost(service.Name);
      if (!string.IsNullOrEmpty(configured))
        return configured;

      return "127.0.0.1";
    }

    private static string? TryGetDockerHost(string? value)
    {
      if (string.IsNullOrWhiteSpace(value) || value == "native")
        return null;

      if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        return uri.Host;

      return value.Contains("://", StringComparison.Ordinal) ? null : value;
    }

    #endregion
  }
}
