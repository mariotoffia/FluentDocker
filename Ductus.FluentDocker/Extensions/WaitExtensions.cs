using System;
using System.Linq;
using System.Threading;
using Docker.DotNet;

namespace Ductus.FluentDocker.Extensions
{
  internal static class WaitExtensions
  {
    internal static void WaitForProcess(this DockerClient client, string id, string process, long millisTimeout)
    {
      do
      {
        try
        {
          var proc = client.ContainerProcesses(id, null);
          if (null != proc.Rows && proc.Rows.Any(x => x.Command == process))
          {
            return;
          }
        }
        catch (Exception)
        {
          // Ignore
        }

        Thread.Sleep(1000);
        millisTimeout -= 1000;
      } while (millisTimeout > 0);

      throw new FluentDockerException($"Timeout while waiting for process {process}");
    }
  }
}