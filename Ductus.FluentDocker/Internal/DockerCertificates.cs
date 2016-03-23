using System.Security.Cryptography.X509Certificates;
using Ductus.FluentDocker.Extensions;

namespace Ductus.FluentDocker.Internal
{
  internal class DockerCertificates
  {
    public const string DefaultCaCertName = "ca.pem";
    public const string DefaultClientCertName = "cert.pem";
    public const string DefaultClientKeyName = "key.pem";

    internal DockerCertificates(string dockerCertPath)
    {
      CaCertificate = dockerCertPath.ToCertificate(DefaultCaCertName);
      ClientCertificate = dockerCertPath.ToCertificate(DefaultClientCertName, DefaultClientKeyName);
    }

    internal X509Certificate2 CaCertificate { get; }
    internal X509Certificate2 ClientCertificate { get; }
  }
}