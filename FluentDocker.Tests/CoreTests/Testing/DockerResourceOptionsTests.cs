using System;
using FluentDocker.Testing.Core;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class DockerResourceOptionsTests
  {
    [Fact]
    public void DefaultValues_AreReasonable()
    {
      var options = new DockerResourceOptions();

      Assert.True(options.Driver.UseDefault);
      Assert.True(options.ForceRemoveOnDispose);
      Assert.True(options.CaptureLogsOnFailure);
      Assert.Equal(TimeSpan.FromMinutes(2), options.InitializationTimeout);
      Assert.Equal(200, options.MaxDiagnosticLogLines);
    }

    [Fact]
    public void AllProperties_AreSettable()
    {
      var options = new DockerResourceOptions
      {
        Driver = DriverSelection.DockerApi(),
        ForceRemoveOnDispose = false,
        InitializationTimeout = TimeSpan.FromSeconds(30),
        CaptureLogsOnFailure = false,
        MaxDiagnosticLogLines = 50
      };

      Assert.Equal("docker-api", options.Driver.DriverId);
      Assert.False(options.ForceRemoveOnDispose);
      Assert.Equal(TimeSpan.FromSeconds(30), options.InitializationTimeout);
      Assert.False(options.CaptureLogsOnFailure);
      Assert.Equal(50, options.MaxDiagnosticLogLines);
    }

    [Fact]
    public void InitializationTimeout_Zero_Throws()
    {
      var opts = new DockerResourceOptions();
      Assert.Throws<ArgumentOutOfRangeException>(() => opts.InitializationTimeout = TimeSpan.Zero);
    }

    [Fact]
    public void InitializationTimeout_Negative_Throws()
    {
      var opts = new DockerResourceOptions();
      Assert.Throws<ArgumentOutOfRangeException>(() => opts.InitializationTimeout = TimeSpan.FromSeconds(-1));
    }

    [Fact]
    public void TeardownTimeout_Zero_Throws()
    {
      var opts = new DockerResourceOptions();
      Assert.Throws<ArgumentOutOfRangeException>(() => opts.TeardownTimeout = TimeSpan.Zero);
    }

    [Fact]
    public void TeardownTimeout_Negative_Throws()
    {
      var opts = new DockerResourceOptions();
      Assert.Throws<ArgumentOutOfRangeException>(() => opts.TeardownTimeout = TimeSpan.FromSeconds(-1));
    }

    [Fact]
    public void MaxDiagnosticLogLines_Negative_Throws()
    {
      var opts = new DockerResourceOptions();
      Assert.Throws<ArgumentOutOfRangeException>(() => opts.MaxDiagnosticLogLines = -1);
    }

    [Fact]
    public void MaxDiagnosticLogLines_Zero_Allowed()
    {
      var opts = new DockerResourceOptions { MaxDiagnosticLogLines = 0 };
      Assert.Equal(0, opts.MaxDiagnosticLogLines);
    }
  }
}
