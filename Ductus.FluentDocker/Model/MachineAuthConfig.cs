namespace Ductus.FluentDocker.Model
{
  public sealed class MachineAuthConfig
  {
    public string CertDir { get; set; }
    public string CaCertPath { get; set; }
    public string ClientKeyPath { get; set; }
    public string ClientCertPath { get; set; }
  }
}