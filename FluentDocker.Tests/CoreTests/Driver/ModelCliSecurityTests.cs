using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Security tests (C5): model references carrying shell metacharacters MUST be
  /// neutralized by argument quoting so nothing can break out of a single argv
  /// token on the CLI control plane (pull / rm / configure / load / run). Process
  /// execution is already shell-free (<c>UseShellExecute=false</c>); this is
  /// defense-in-depth for arg boundaries.
  ///
  /// The inference data plane is HTTP (the OpenAI-compatible :12434 endpoint):
  /// prompts ride inside a JSON request body, never a shell command line, so
  /// shell-metacharacter injection is structurally impossible there and needs no
  /// dedicated test.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelCliSecurityTests
  {
    private static DriverContext Ctx => new("docker");

    // Mirrors DockerCliDriverBase.QuoteArgumentIfNeeded so we can assert the exact
    // neutralized token appears in the emitted command.
    private static readonly char[] Meta = " \t;&|><\"'$`!*?".ToCharArray();

    private static string Quote(string arg)
    {
      if (string.IsNullOrEmpty(arg))
        return "\"\"";
      if (arg.IndexOfAny(Meta) < 0)
        return arg;
      return "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    [Theory]
    [InlineData("ai/x:v$(whoami)")]
    [InlineData("ai/x:tag`id`")]
    public async Task ModelReference_WithMetacharacters_IsQuotedInRemoveCommand(string reference)
    {
      var driver = new CapturingMgmtDriver();
      var parsed = ModelReference.Parse(reference);

      await driver.RemoveAsync(Ctx, parsed, false, TestContext.Current.CancellationToken);

      Assert.Contains(Quote(parsed.ToString()), driver.LastCommand);
    }

    /// <summary>A management driver that captures the last emitted command.</summary>
    private sealed class CapturingMgmtDriver : DockerCliModelManagementDriver
    {
      public string LastCommand { get; private set; }

      public CapturingMgmtDriver() : base(null)
      {
      }

      protected override Task<SimpleCommandResult> RunAsync(string arguments, CancellationToken cancellationToken)
      {
        LastCommand = arguments;
        return Task.FromResult(new SimpleCommandResult { Success = true, Output = string.Empty, ExitCode = 0 });
      }
    }
  }
}
