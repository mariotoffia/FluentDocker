using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using Docker.DotNet;

namespace Ductus.FluentDocker.Internal
{
  internal class DockerCertificateCredentials : Credentials
  {
    private readonly WebRequestHandler _handler;

    internal DockerCertificateCredentials(params X509Certificate2 []clientCertificates)
    {
      _handler = new WebRequestHandler()
      {
        ClientCertificateOptions = ClientCertificateOption.Manual,
        UseDefaultCredentials = false,
        UseProxy = false
      };

      foreach (X509Certificate2 cert in clientCertificates)
      {
        _handler.ClientCertificates.Add(cert);
      }
    }

    public override HttpMessageHandler Handler => _handler;

    public override bool IsTlsCredentials()
    {
      return true;
    }

    public override void Dispose()
    {
      _handler.Dispose();
    }
  }
}
