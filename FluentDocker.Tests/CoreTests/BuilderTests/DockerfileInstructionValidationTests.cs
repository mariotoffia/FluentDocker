using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Model.Builders.FileBuilder;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  [Trait("Category", "Unit")]
  public sealed class DockerfileInstructionValidationTests
  {
    [Theory]
    [MemberData(nameof(NewlineInjectedCommands))]
    public void RawInstructionCommand_WithEmbeddedNewline_Throws(Func<object> createCommand)
    {
      var ex = Assert.ThrowsAny<Exception>(() => createCommand());

      Assert.True(ex is ArgumentException or FluentDockerException);
      Assert.Contains("control characters", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<Func<object>> NewlineInjectedCommands() =>
        new()
        {
          () => new RunCommand("x\nRUN evil"),
          () => new ArgCommand("x\nRUN evil"),
          () => new ArgCommand("name", "x\nRUN evil"),
          () => new UserCommand("x\nRUN evil"),
          () => new WorkdirCommand("x\nRUN evil"),
          () => new FromCommand("x\nRUN evil"),
          () => new FromCommand("alpine", "x\nRUN evil"),
          () => new HealthCheckCommand("x\nRUN evil"),
#pragma warning disable CS0618
          () => new MaintainerCommand("me\nRUN evil")
#pragma warning restore CS0618
        };

    [Fact]
    public void RunCommand_WithControlCharacter_Throws()
    {
      var ex = Assert.ThrowsAny<Exception>(() => new RunCommand("echo \u0001"));

      Assert.Contains("control characters", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExposeCommand_WithEmbeddedNewline_Throws()
    {
      var ex = Assert.Throws<FluentDockerException>(() => new ExposeCommand("80/tcp\nRUN evil"));

      Assert.Contains("control characters", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExposeCommand_WithInvalidProtocol_Throws()
    {
      var ex = Assert.Throws<FluentDockerException>(() => new ExposeCommand("80/bogus"));

      Assert.Contains("tcp, udp, or sctp", ex.Message);
    }

    [Theory]
    [InlineData("80")]
    [InlineData("80/tcp")]
    [InlineData("53/udp")]
    [InlineData("80/sctp")]
    public void ExposeCommand_WithValidProtocol_Renders(string port)
    {
      var dockerfile = new ExposeCommand(port).ToString();

      Assert.Equal($"EXPOSE {port}", dockerfile);
    }

    [Fact]
    public async Task RunCommand_WithBackslashContinuation_Renders()
    {
      var dockerfile = await new DockerfileBuilder()
          .WorkingFolder(".out/dockerfile-run-continuation")
          .UseParent("alpine")
          .Run(@"echo one \ && echo two")
          .ToDockerfileStringAsync(TestContext.Current.CancellationToken);

      Assert.Contains(@"RUN echo one \ && echo two", dockerfile);
    }

    // BF-6: the guard's remedy must be achievable — a line continuation contains a newline,
    // which this very guard rejects, so the message may only suggest multiple Run() calls.
    [Fact]
    public void RunCommand_WithEmbeddedNewline_MessageDoesNotSuggestLineContinuations()
    {
      var ex = Assert.Throws<FluentDockerException>(() => new RunCommand("x\nRUN evil"));

      Assert.DoesNotContain("line continuation", ex.Message, StringComparison.OrdinalIgnoreCase);
      Assert.Contains("multiple Run() calls", ex.Message);
    }

    // BF-5: empty EXPOSE/VOLUME argument lists would render invalid Dockerfile instructions
    // ("EXPOSE " / "VOLUME []"), so they are rejected at the fluent call.
    [Fact]
    public void ExposeCommand_EmptyOrNullPorts_ThrowsArgumentException()
    {
      Assert.Throws<ArgumentException>(() => new ExposeCommand(Array.Empty<int>()));
      Assert.Throws<ArgumentException>(() => new ExposeCommand((int[])null!));
      Assert.Throws<ArgumentException>(() => new ExposeCommand((string[])null!));
      Assert.Throws<ArgumentException>(() => new ExposeCommand(Array.Empty<string>()));
    }

    [Fact]
    public void VolumeCommand_EmptyOrNullMountpoints_ThrowsArgumentException()
    {
      Assert.Throws<ArgumentException>(() => new VolumeCommand());
      Assert.Throws<ArgumentException>(() =>
          new VolumeCommand((FluentDocker.Model.Common.TemplateString[])null!));
    }

    // BF-7: rendering must not contain double spaces and negative retries are rejected.
    [Fact]
    public void HealthCheckCommand_DefaultOptions_RendersWithoutDoubleSpaces()
    {
      var rendered = new HealthCheckCommand("curl -f http://localhost/ || exit 1").ToString();

      Assert.Equal(
          "HEALTHCHECK --interval=30s --timeout=30s --start-period=0s CMD curl -f http://localhost/ || exit 1",
          rendered);
      Assert.DoesNotContain("  ", rendered);
    }

    [Fact]
    public void HealthCheckCommand_NonDefaultRetries_RendersRetriesOption()
    {
      var rendered = new HealthCheckCommand("ping", retries: 5).ToString();

      Assert.Contains("--retries=5", rendered);
      Assert.DoesNotContain("  ", rendered);
    }

    [Fact]
    public void HealthCheckCommand_NegativeRetries_ThrowsArgumentOutOfRangeException()
    {
      var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new HealthCheckCommand("ping", retries: -1));

      Assert.Equal("retries", ex.ParamName);
    }
  }
}
