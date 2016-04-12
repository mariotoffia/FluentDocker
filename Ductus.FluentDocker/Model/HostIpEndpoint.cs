using System.Net;

namespace Ductus.FluentDocker.Model
{
  public class HostIpEndpoint : IPEndPoint
  {
    private string _hostIp;
    private string _hostPort;

    public HostIpEndpoint() : base(0,0)
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
      get { return _hostIp; }
      set
      {
        _hostIp = value;

        IPAddress addr;
        if (!IPAddress.TryParse(value, out addr))
        {
          addr = IPAddress.None;
        }

        Address = addr;
      }
    }

    public string HostPort
    {
      get { return _hostPort; }
      set
      {
        _hostPort = value;

        int port;
        int.TryParse(value, out port);
        Port = port;
      }
    }
  }
}
