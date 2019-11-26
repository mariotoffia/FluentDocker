namespace Ductus.FluentDocker.Model.Containers
{
  public interface ICertificatePaths
  {
    string CaCertificate { get; }
    string ClientCertificate { get; }
    string ClientKey { get; }
  }

  public sealed class CertificatePaths : ICertificatePaths
  {
    public string CaCertificate { get; set; }
    public string ClientCertificate { get; set; }
    public string ClientKey { get; set; }
  }
}
