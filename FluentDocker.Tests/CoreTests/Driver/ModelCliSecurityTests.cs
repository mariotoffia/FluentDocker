using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Security tests (C5): user-controllable values carrying shell metacharacters
  /// MUST be neutralized by argument quoting so nothing can break out of a single
  /// argv token on the CLI control plane (pull / inspect / rm / tag / push /
  /// package). Process execution is already shell-free
  /// (<c>UseShellExecute=false</c>); this is defense-in-depth for arg boundaries.
  ///
  /// Two distinct injection surfaces are exercised:
  /// <list type="bullet">
  /// <item><b>Model references</b> (pull/inspect/tag/push) — these flow through
  /// <see cref="ModelReference.Parse"/>, which rejects whitespace and a second
  /// <c>:</c> in a tag, so the metacharacter payloads here are limited to those
  /// that survive parsing (e.g. <c>$(...)</c>, <c>`...`</c>, <c>;</c>, <c>|</c>,
  /// <c>&amp;</c>, <c>"</c>).</item>
  /// <item><b>Raw path/string options</b> (<c>--gguf</c>, <c>--license</c>) — these
  /// are NOT parsed, so the full payload set (spaces, quotes, newlines) applies.</item>
  /// </list>
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
    // neutralized token appears in the emitted command. The production code quotes
    // when ANY of these metacharacters is present OR the value contains any other
    // whitespace/control char (e.g. '\n'); this helper mirrors both rules.
    private static readonly char[] Meta = " \t;&|><\"'$`!*?".ToCharArray();

    private static string Quote(string arg)
    {
      if (string.IsNullOrEmpty(arg))
        return "\"\"";

      var needsQuoting = arg.IndexOfAny(Meta) >= 0;
      if (!needsQuoting)
      {
        foreach (var c in arg)
        {
          if (char.IsWhiteSpace(c) || char.IsControl(c))
          {
            needsQuoting = true;
            break;
          }
        }
      }

      if (!needsQuoting)
        return arg;

      return "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    // Reference payloads that survive ModelReference.Parse (no whitespace, no
    // second ':' in the tag) yet still carry shell metacharacters in the tag.
    private const string RefInject1 = "ai/x:v$(whoami)";
    private const string RefInject2 = "ai/x:tag`id`";
    private const string RefInject3 = "ai/x:t;rm";
    private const string RefInject4 = "ai/x:t&&id";
    private const string RefInject5 = "ai/x:t|cat";
    private const string RefInject6 = "ai/x:t\"q";

    // Raw path/string payloads for --gguf / --license (no parsing, full payload set).
    private const string RawSpace = "a b";
    private const string RawQuote = "a\"b";
    private const string RawNewline = "a\nb";
    private const string RawSemi = "model;rm -rf /";
    private const string RawSub = "$(whoami)";
    private const string RawBacktick = "`id`";

    /// <summary>
    /// Asserts the captured command contains <paramref name="raw"/> in its quoted
    /// form AND that the raw value's dangerous metacharacters do not appear outside
    /// a quoted token (i.e. no shell-active <c>$(</c>/<c>`</c>/<c>;</c>/<c>|</c>/
    /// <c>&amp;</c> escaped the argv boundary because of this value).
    /// </summary>
    private static void AssertNeutralized(string command, string raw)
    {
      Assert.NotNull(command);

      var quoted = Quote(raw);
      Assert.Contains(quoted, command);

      // When the value needed quoting it must NOT also appear verbatim/unquoted.
      // (Quote() == raw only for benign values that need no quoting at all.)
      if (quoted != raw)
        Assert.DoesNotContain(" " + raw, command);

      // Defense-in-depth scan: the dangerous tokens introduced by the payload must
      // live inside a quoted span, never bare on the command line. Strip every
      // quoted "..." span, then confirm none of the payload's metacharacters leaked.
      var outsideQuotes = RemoveQuotedSpans(command);
      foreach (var marker in new[] { "$(", "`", ";", "|", "&&", "&", ">", "<" })
      {
        if (raw.Contains(marker, StringComparison.Ordinal))
          Assert.DoesNotContain(marker, outsideQuotes);
      }
    }

    /// <summary>Removes every <c>"..."</c> span (honoring <c>\"</c> escapes) so the
    /// remainder is only the unquoted portion of the command line.</summary>
    private static string RemoveQuotedSpans(string command)
    {
      var sb = new System.Text.StringBuilder(command.Length);
      var inQuote = false;
      for (var i = 0; i < command.Length; i++)
      {
        var c = command[i];
        if (c == '\\' && i + 1 < command.Length && inQuote)
        {
          i++; // skip the escaped char while inside a quoted span
          continue;
        }

        if (c == '"')
        {
          inQuote = !inQuote;
          continue;
        }

        if (!inQuote)
          sb.Append(c);
      }

      return sb.ToString();
    }

    [Theory]
    [InlineData(RefInject1)]
    [InlineData(RefInject2)]
    [InlineData(RefInject3)]
    [InlineData(RefInject4)]
    [InlineData(RefInject5)]
    [InlineData(RefInject6)]
    public async Task ModelReference_WithMetacharacters_IsQuotedInRemoveCommand(string reference)
    {
      var driver = new CapturingMgmtDriver();
      var parsed = ModelReference.Parse(reference);

      await driver.RemoveAsync(Ctx, parsed, false, TestContext.Current.CancellationToken);

      AssertNeutralized(driver.LastCommand, parsed.ToString());
    }

    [Theory]
    [InlineData(RefInject1)]
    [InlineData(RefInject2)]
    [InlineData(RefInject3)]
    [InlineData(RefInject4)]
    [InlineData(RefInject5)]
    [InlineData(RefInject6)]
    public async Task ModelReference_WithMetacharacters_IsQuotedInPullCommand(string reference)
    {
      var driver = new CapturingMgmtDriver();
      var parsed = ModelReference.Parse(reference);

      await driver.PullAsync(Ctx, parsed, null, TestContext.Current.CancellationToken);

      AssertNeutralized(driver.LastCommand, parsed.ToString());
    }

    [Theory]
    [InlineData(RefInject1)]
    [InlineData(RefInject2)]
    [InlineData(RefInject3)]
    [InlineData(RefInject4)]
    [InlineData(RefInject5)]
    [InlineData(RefInject6)]
    public async Task ModelReference_WithMetacharacters_IsQuotedInInspectCommand(string reference)
    {
      var driver = new CapturingMgmtDriver();
      var parsed = ModelReference.Parse(reference);

      await driver.InspectAsync(Ctx, parsed, TestContext.Current.CancellationToken);

      AssertNeutralized(driver.LastCommand, parsed.ToString());
    }

    [Theory]
    [InlineData(RefInject1)]
    [InlineData(RefInject2)]
    [InlineData(RefInject3)]
    [InlineData(RefInject4)]
    [InlineData(RefInject5)]
    [InlineData(RefInject6)]
    public async Task ModelReference_WithMetacharacters_IsQuotedInPushCommand(string reference)
    {
      var driver = new CapturingMgmtDriver();
      var parsed = ModelReference.Parse(reference);

      await driver.PushAsync(Ctx, parsed, TestContext.Current.CancellationToken);

      AssertNeutralized(driver.LastCommand, parsed.ToString());
    }

    [Theory]
    [InlineData(RefInject1)]
    [InlineData(RefInject2)]
    [InlineData(RefInject3)]
    [InlineData(RefInject4)]
    [InlineData(RefInject5)]
    [InlineData(RefInject6)]
    public async Task TagSource_WithMetacharacters_IsQuotedInTagCommand(string reference)
    {
      var driver = new CapturingMgmtDriver();
      var source = ModelReference.Parse(reference);
      var target = ModelReference.Parse("ai/safe:latest");

      await driver.TagAsync(Ctx, source, target, TestContext.Current.CancellationToken);

      AssertNeutralized(driver.LastCommand, source.ToString());
    }

    [Theory]
    [InlineData(RefInject1)]
    [InlineData(RefInject2)]
    [InlineData(RefInject3)]
    [InlineData(RefInject4)]
    [InlineData(RefInject5)]
    [InlineData(RefInject6)]
    public async Task TagTarget_WithMetacharacters_IsQuotedInTagCommand(string reference)
    {
      var driver = new CapturingMgmtDriver();
      var source = ModelReference.Parse("ai/safe:latest");
      var target = ModelReference.Parse(reference);

      await driver.TagAsync(Ctx, source, target, TestContext.Current.CancellationToken);

      AssertNeutralized(driver.LastCommand, target.ToString());
    }

    [Theory]
    [InlineData(RawSpace)]
    [InlineData(RawQuote)]
    [InlineData(RawNewline)]
    [InlineData(RawSemi)]
    [InlineData(RawSub)]
    [InlineData(RawBacktick)]
    public async Task PackageGgufPath_WithMetacharacters_IsQuotedInPackageCommand(string ggufPath)
    {
      var driver = new CapturingMgmtDriver();
      var request = new ModelPackageRequest
      {
        GgufPath = ggufPath,
        Target = ModelReference.Parse("ai/safe:latest"),
      };

      await driver.PackageAsync(Ctx, request, TestContext.Current.CancellationToken);

      AssertNeutralized(driver.LastCommand, ggufPath);
    }

    [Theory]
    [InlineData(RawSpace)]
    [InlineData(RawQuote)]
    [InlineData(RawNewline)]
    [InlineData(RawSemi)]
    [InlineData(RawSub)]
    [InlineData(RawBacktick)]
    public async Task PackageLicense_WithMetacharacters_IsQuotedInPackageCommand(string license)
    {
      var driver = new CapturingMgmtDriver();
      var request = new ModelPackageRequest
      {
        GgufPath = "/models/model.gguf",
        License = license,
        Target = ModelReference.Parse("ai/safe:latest"),
      };

      await driver.PackageAsync(Ctx, request, TestContext.Current.CancellationToken);

      AssertNeutralized(driver.LastCommand, license);
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

      protected override async IAsyncEnumerable<string> RunStreamingAsync(string arguments,
          [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
      {
        LastCommand = arguments;
        await Task.CompletedTask;
        yield break;
      }
    }
  }
}
