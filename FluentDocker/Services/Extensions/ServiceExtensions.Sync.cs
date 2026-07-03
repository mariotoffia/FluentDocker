using System.Net;
using System.Threading.Tasks;
using FluentDocker.Model.Containers;

namespace FluentDocker.Services.Extensions
{
  public static partial class ServiceExtensions
  {
    #region Sync Wrappers (for backward compatibility patterns)

    /// <summary>
    /// Gets the container configuration synchronously.
    /// </summary>
    public static Container GetConfiguration(this IContainerService service, bool fresh = false)
    {
      return Task.Run(() => service.GetConfigurationAsync(fresh)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Gets the host-exposed endpoint synchronously.
    /// </summary>
    public static IPEndPoint ToHostExposedEndpoint(this IContainerService service, string portAndProto)
    {
      return Task.Run(() => service.ToHostExposedEndpointAsync(portAndProto)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Gets the host port synchronously.
    /// </summary>
    public static int GetHostPort(this IContainerService service, string portAndProto)
    {
      return Task.Run(() => service.GetHostPortAsync(portAndProto)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Waits for a port synchronously.
    /// </summary>
    public static bool WaitForPort(this IContainerService service, string portAndProto, long timeout = 30000)
    {
      return Task.Run(() => service.WaitForPortAsync(portAndProto, timeout)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Waits for a process synchronously.
    /// </summary>
    public static bool WaitForProcess(this IContainerService service, string processName, long timeout = 30000)
    {
      return Task.Run(() => service.WaitForProcessAsync(processName, timeout)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Waits for HTTP endpoint synchronously.
    /// </summary>
    public static bool WaitForHttp(this IContainerService service, string portAndProto, string path = "/", long timeout = 30000)
    {
      return Task.Run(() => service.WaitForHttpAsync(portAndProto, path, timeout)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Waits for log message synchronously.
    /// </summary>
    public static bool WaitForLogMessage(this IContainerService service, string text, long timeout = 30000)
    {
      return Task.Run(() => service.WaitForLogMessageAsync(text, timeout)).GetAwaiter().GetResult();
    }

    #endregion
  }
}
