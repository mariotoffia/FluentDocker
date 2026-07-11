using FluentDocker.Common;
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
      var result = PodmanCliDriverBase.BuildGlobalArgs(null!);
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
    public void BuildGlobalArgs_CertsOnTcp_FailsClosed()
    {
      var ctx = new DriverContext
      {
        Host = "tcp://remote:2375",
        CertificatePath = "/certs"
      };
      // PDM-MAJ-1: podman CLI cannot honor TLS on tcp://; failing closed prevents a silent
      // plaintext downgrade rather than dropping the certs and connecting insecurely.
      var ex = Assert.Throws<DriverException>(() => PodmanCliDriverBase.BuildGlobalArgs(ctx));
      Assert.Equal(ErrorCodes.General.InvalidArgument, ex.ErrorCode);
    }

    [Fact]
    public void BuildGlobalArgs_VerifyTlsOnTcp_FailsClosed()
    {
      var ctx = new DriverContext { Host = "tcp://remote:2375", VerifyTls = true };
      Assert.Throws<DriverException>(() => PodmanCliDriverBase.BuildGlobalArgs(ctx));
    }

    [Fact]
    public void BuildGlobalArgs_VerifyTlsFalseOnTcp_DoesNotThrow()
    {
      // Explicitly requesting NO verification is fine to honor as plaintext.
      var ctx = new DriverContext { Host = "tcp://remote:2375", VerifyTls = false };
      Assert.Equal("--url tcp://remote:2375", PodmanCliDriverBase.BuildGlobalArgs(ctx));
    }

    [Fact]
    public void BuildGlobalArgs_CertsOnSsh_IgnoredNotThrown()
    {
      // ssh:// tunnels are already authenticated/encrypted, so ignoring the TLS settings is legitimate.
      var ctx = new DriverContext { Host = "ssh://user@remote/run/podman.sock", CertificatePath = "/certs" };
      Assert.Equal("--url ssh://user@remote/run/podman.sock", PodmanCliDriverBase.BuildGlobalArgs(ctx));
    }

    [Fact]
    public void BuildGlobalArgs_CertsOnUnix_IgnoredNotThrown()
    {
      var ctx = new DriverContext { Host = "unix:///run/podman/podman.sock", CertificatePath = "/certs", VerifyTls = true };
      Assert.Equal("--url unix:///run/podman/podman.sock", PodmanCliDriverBase.BuildGlobalArgs(ctx));
    }

    // P-M1: a remote daemon (Host set) is never machine-managed, so a connection-refused error
    // against it must classify as Api.ConnectionFailed, never Machine.NotRunning — regardless of
    // AutoStartMachine or the macOS/Windows platform default that otherwise implies a local VM.
    // FailureCode(DriverContext, string, string) is `protected static`; exposed via a tiny probe
    // subclass (same pattern as AttachProbeDriver in PodmanCliAuditRemediationTests) rather than
    // reflection, since protected statics are directly callable from a derived class.
    private sealed class FailureClassificationProbe : PodmanCliDriverBase
    {
      public static string Classify(DriverContext context, string error, string fallbackCode)
          => FailureCode(context, error, fallbackCode);
    }

    [Theory]
    [InlineData(true, true, false)]   // remote host + explicit auto-start config: gate wins
    [InlineData(true, false, false)]  // remote host, no auto-start config: gate wins over platform default too
    [InlineData(false, true, true)]   // no host, auto-start config set: unchanged, still machine-managed
    public void FailureCode_HostGatesMachineManagedClassification(
        bool setHost, bool setAutoStart, bool expectMachineManaged)
    {
      var context = new DriverContext("podman")
      {
        Host = setHost ? "ssh://prod-host" : null,
        AutoStartMachine = setAutoStart ? new AutoStartMachineConfig() : null
      };

      var code = FailureClassificationProbe.Classify(context, "connection refused", "fallback");

      Assert.Equal(
          expectMachineManaged ? ErrorCodes.Machine.NotRunning : ErrorCodes.Api.ConnectionFailed,
          code);
    }

    [Fact]
    public void FailureCode_NoHostNoAutoStart_MatchesPlatformMachineManagementDefault()
    {
      // Unchanged legacy behavior: with neither a remote host nor an explicit auto-start config,
      // classification still follows PodmanCliDriverPack.MachineManagementApplies() (true on
      // macOS/Windows, false on native Linux).
      var context = new DriverContext("podman");

      var code = FailureClassificationProbe.Classify(context, "connection refused", "fallback");

      Assert.Equal(
          PodmanCliDriverPack.MachineManagementApplies() ? ErrorCodes.Machine.NotRunning : ErrorCodes.Api.ConnectionFailed,
          code);
    }
  }
}
