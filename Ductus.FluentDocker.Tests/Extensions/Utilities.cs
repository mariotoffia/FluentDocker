using System;
using System.Diagnostics;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Tests.Extensions
{
  public static class Utilities
  {
    public static void LinuxMode()
    {
      if (FdOs.IsWindows())
        Info.LinuxDaemon(null);
    }

    /// <summary>
    ///   Tries really hard to pull a image.
    /// </summary>
    /// <param name="image">The image to pull.</param>
    /// <param name="ts">The amount of time to maximum try to pull.</param>
    /// <remarks>
    ///   This method is for appveyor that seems to sometimes have a hard time
    ///   connecting to docker registry (connection refused). Thus this method
    ///   re-tries several times force pull the image.
    /// </remarks>
    public static void EnsureImage(string image, TimeSpan ts)
    {
      var sw = new Stopwatch();
      sw.Start();
      while (sw.Elapsed < ts)
      {
        try
        {
          var pull = Client.Pull(null, image);
          if (pull.Success)
          {
            Logger.Log($"Successfully pulled {image}");
            break;
          }

          Logger.Log($"Failed to pull {image} error: {pull.Error} elapsed: {sw.Elapsed}");
        }
        catch (Exception e)
        {
          Logger.Log($"Failed to pull {image},  exception: {e.Message}");
        }
      }
    }


    /// <summary>
    ///   Sets the docker daemon to linux if on windows system.
    /// </summary>
    /// <param name="host">The uri to host, may be null for default.</param>
    /// <param name="certificates">The certificates to communicate, many be null.</param>
    public static void LinuxMode(this DockerUri host, ICertificatePaths certificates = null)
    {
      if (FdOs.IsWindows())
        host.LinuxDaemon(certificates);
    }
  }
}
