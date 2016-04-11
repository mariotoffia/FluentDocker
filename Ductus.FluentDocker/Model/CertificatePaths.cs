namespace Ductus.FluentDocker.Model
{
  public sealed class CertificatePaths
  {
    public string CaCertificate { get; set; }
    public string ClientCertificate { get; set; }
    public string ClientKey { get; set; }
  }
}
