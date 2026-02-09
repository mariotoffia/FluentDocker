using System.Net;

namespace FluentDocker.Model.Containers
{
  public class HostIpEndpoint : IPEndPoint
  {
    private string _hostIp;
    private string _hostPort;

    public HostIpEndpoint() : base(0, 0)
    {
    }

    public HostIpEndpoint(long address, int port) : base(address, port)
    {
    }

    public HostIpEndpoint(IPAddress address, int port) : base(address, port)
    {
    }

    public string HostIp
    {
      get => _hostIp;
      set
      {
        _hostIp = value;

        if (!IPAddress.TryParse(value, out var addr))
        {
          addr = IPAddress.None;
        }

        Address = addr;
      }
    }

    public string HostPort
    {
      get => _hostPort;
      set
      {
        _hostPort = value;

        int.TryParse(value, out var port);
        Port = port;
      }
    }
  }
}
