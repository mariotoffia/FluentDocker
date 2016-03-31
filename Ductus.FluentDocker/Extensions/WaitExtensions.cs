using System;
using System.Net.Sockets;
using System.Threading;
using Docker.DotNet;
using System.Linq;

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

    internal static void WaitForPort(this string host, int port, long millisTimeout)
    {
      using (var s = new Socket(SocketType.Stream, ProtocolType.Tcp))
      {
        long totalWait = 0;
        while (totalWait < millisTimeout)
        {
          try
          {
            s.Connect(host, port);
            break;
          }
          catch (Exception)
          {
            Thread.Sleep(1000);
            totalWait += 1000;
            if (totalWait >= millisTimeout)
            {
              throw new Exception($"Timeout waiting for port {port}");
            }
          }
        }
      }
    }
  }
}
