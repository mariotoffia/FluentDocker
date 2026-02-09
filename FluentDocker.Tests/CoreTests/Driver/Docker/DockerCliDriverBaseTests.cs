using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  [Trait("Category", "Unit")]
  public class DockerCliDriverBaseTests
  {
    [Fact]
    public void BuildGlobalArgs_NullContext_ReturnsEmpty()
    {
      var result = DockerCliDriverBase.BuildGlobalArgs(null);
      Assert.Equal("", result);
    }

    [Fact]
    public void BuildGlobalArgs_NoHost_ReturnsEmpty()
    {
      var ctx = new DriverContext();
      var result = DockerCliDriverBase.BuildGlobalArgs(ctx);
      Assert.Equal("", result);
    }

    [Fact]
    public void BuildGlobalArgs_EmptyHost_ReturnsEmpty()
    {
      var ctx = new DriverContext { Host = "" };
      var result = DockerCliDriverBase.BuildGlobalArgs(ctx);
      Assert.Equal("", result);
    }

    [Fact]
    public void BuildGlobalArgs_WithHost_ReturnsHFlag()
    {
      var ctx = new DriverContext { Host = "tcp://remote:2375" };
      var result = DockerCliDriverBase.BuildGlobalArgs(ctx);
      Assert.Equal("-H tcp://remote:2375", result);
    }

    [Fact]
    public void BuildGlobalArgs_WithHostOnly_NoCertFlags()
    {
      var ctx = new DriverContext { Host = "tcp://remote:2375" };
      var result = DockerCliDriverBase.BuildGlobalArgs(ctx);
      Assert.DoesNotContain("--tls", result);
      Assert.DoesNotContain("cert", result);
    }

    [Fact]
    public void BuildGlobalArgs_WithCertsAndVerify_IncludesTlsVerifyFlags()
    {
      var ctx = new DriverContext
      {
        Host = "tcp://remote:2376",
        CertificatePath = "/certs",
        VerifyTls = true
      };
      var result = DockerCliDriverBase.BuildGlobalArgs(ctx);
      Assert.Contains("-H tcp://remote:2376", result);
      Assert.Contains("--tlsverify", result);
      Assert.Contains("--tlscacert", result);
      Assert.Contains("ca.pem", result);
      Assert.Contains("--tlscert", result);
      Assert.Contains("cert.pem", result);
      Assert.Contains("--tlskey", result);
      Assert.Contains("key.pem", result);
    }

    [Fact]
    public void BuildGlobalArgs_WithCertsAndVerify_UsesPathCombine()
    {
      var ctx = new DriverContext
      {
        Host = "tcp://remote:2376",
        CertificatePath = "/certs",
        VerifyTls = true
      };
      var result = DockerCliDriverBase.BuildGlobalArgs(ctx);

      var expectedCa = System.IO.Path.Combine("/certs", "ca.pem");
      var expectedCert = System.IO.Path.Combine("/certs", "cert.pem");
      var expectedKey = System.IO.Path.Combine("/certs", "key.pem");

      Assert.Contains($"--tlscacert {expectedCa}", result);
      Assert.Contains($"--tlscert {expectedCert}", result);
      Assert.Contains($"--tlskey {expectedKey}", result);
    }

    [Fact]
    public void BuildGlobalArgs_WithCertsNoVerify_UsesTlsNotTlsverify()
    {
      var ctx = new DriverContext
      {
        Host = "tcp://remote:2376",
        CertificatePath = "/certs",
        VerifyTls = false
      };
      var result = DockerCliDriverBase.BuildGlobalArgs(ctx);
      Assert.Contains("--tls ", result);
      Assert.DoesNotContain("--tlsverify", result);
    }

    [Fact]
    public void BuildGlobalArgs_NoCertPath_NoTlsFlags()
    {
      var ctx = new DriverContext
      {
        Host = "tcp://remote:2376",
        CertificatePath = null,
        VerifyTls = true
      };
      var result = DockerCliDriverBase.BuildGlobalArgs(ctx);
      Assert.Equal("-H tcp://remote:2376", result);
      Assert.DoesNotContain("--tls", result);
    }

    [Fact]
    public void BuildGlobalArgs_EmptyCertPath_NoTlsFlags()
    {
      var ctx = new DriverContext
      {
        Host = "tcp://remote:2376",
        CertificatePath = "",
        VerifyTls = true
      };
      var result = DockerCliDriverBase.BuildGlobalArgs(ctx);
      Assert.Equal("-H tcp://remote:2376", result);
    }

    [Fact]
    public void BuildGlobalArgs_FullCombination_CorrectOrder()
    {
      var ctx = new DriverContext
      {
        Host = "tcp://remote:2376",
        CertificatePath = "/certs",
        VerifyTls = true
      };
      var result = DockerCliDriverBase.BuildGlobalArgs(ctx);

      // Host flag should come first
      Assert.StartsWith("-H tcp://remote:2376", result);

      // TLS flags should follow
      var hostEnd = result.IndexOf("tcp://remote:2376") + "tcp://remote:2376".Length;
      var afterHost = result.Substring(hostEnd);
      Assert.Contains("--tlsverify", afterHost);
    }
  }
}
