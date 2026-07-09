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
  }
}
