using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  [Trait("Category", "Unit")]
  public class PodmanCliDriverBaseTests
  {
    [Fact]
    public void BuildGlobalArgs_NullContext_ReturnsEmpty()
    {
      var result = PodmanCliDriverBase.BuildGlobalArgs(null);
      Assert.Equal("", result);
    }

    [Fact]
    public void BuildGlobalArgs_NoHost_ReturnsEmpty()
    {
      var ctx = new DriverContext();
      var result = PodmanCliDriverBase.BuildGlobalArgs(ctx);
      Assert.Equal("", result);
    }

    [Fact]
    public void BuildGlobalArgs_EmptyHost_ReturnsEmpty()
    {
      var ctx = new DriverContext { Host = "" };
      var result = PodmanCliDriverBase.BuildGlobalArgs(ctx);
      Assert.Equal("", result);
    }

    [Fact]
    public void BuildGlobalArgs_WithHost_ReturnsUrlFlag()
    {
      var ctx = new DriverContext { Host = "tcp://remote:2375" };
      var result = PodmanCliDriverBase.BuildGlobalArgs(ctx);
      Assert.Equal("--url tcp://remote:2375", result);
    }

    [Fact]
    public void BuildGlobalArgs_WithUnixSocket_ReturnsUrlFlag()
    {
      var ctx = new DriverContext { Host = "unix:///run/podman/podman.sock" };
      var result = PodmanCliDriverBase.BuildGlobalArgs(ctx);
      Assert.Equal("--url unix:///run/podman/podman.sock", result);
    }

    [Fact]
    public void BuildGlobalArgs_CertsIgnored_NoCertFlags()
    {
      var ctx = new DriverContext
      {
        Host = "tcp://remote:2375",
        CertificatePath = "/certs"
      };
      var result = PodmanCliDriverBase.BuildGlobalArgs(ctx);
      Assert.Equal("--url tcp://remote:2375", result);
      Assert.DoesNotContain("--tls", result);
      Assert.DoesNotContain("cert", result);
    }

    [Fact]
    public void BuildGlobalArgs_CertsAndVerifyIgnored_NoCertFlags()
    {
      var ctx = new DriverContext
      {
        Host = "tcp://remote:2375",
        CertificatePath = "/certs",
        VerifyTls = true
      };
      var result = PodmanCliDriverBase.BuildGlobalArgs(ctx);
      Assert.Equal("--url tcp://remote:2375", result);
      Assert.DoesNotContain("--tls", result);
    }
  }
}
