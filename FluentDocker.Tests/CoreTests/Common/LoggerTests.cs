using System.Diagnostics;
using System.Reflection;
using FluentDocker.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  /// <summary>
  /// Unit tests for <see cref="Logger"/>.
  /// </summary>
  [Trait("Category", "Unit")]
  public class LoggerTests
  {
    private static readonly FieldInfo EnabledField =
      typeof(Logger).GetField("Enabled", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static bool GetEnabled() => (bool)EnabledField.GetValue(null)!;
    private static void SetEnabled(bool value) => EnabledField.SetValue(null, value);

    [Fact]
    public void Enabled_DefaultsToTrue()
    {
      // Assert
      Assert.True(GetEnabled());
    }

    [Fact]
    public void Log_WhenEnabled_WritesToTrace()
    {
      // Arrange
      var listener = new CapturingTraceListener();
      Trace.Listeners.Add(listener);
      try
      {
        SetEnabled(true);

        // Act
        Logger.Log("hello from test");

        // Assert — other parallel tests may also write to Trace,
        // so check that our specific message is present rather than asserting exactly one.
        var match = Assert.Single(listener.Messages,
            m => m.Message == "hello from test" && m.Category == "FluentDocker");
      }
      finally
      {
        Trace.Listeners.Remove(listener);
        SetEnabled(true);
      }
    }

    [Fact]
    public void Log_WhenDisabled_DoesNotWriteToTrace()
    {
      // Arrange
      var listener = new CapturingTraceListener();
      Trace.Listeners.Add(listener);
      try
      {
        SetEnabled(false);

        // Act
        Logger.Log("this should not appear");

        // Assert
        Assert.Empty(listener.Messages);
      }
      finally
      {
        Trace.Listeners.Remove(listener);
        SetEnabled(true);
      }
    }

    /// <summary>
    /// A simple <see cref="TraceListener"/> that captures calls to
    /// <see cref="TraceListener.WriteLine(string?, string?)"/>.
    /// </summary>
    private sealed class CapturingTraceListener : TraceListener
    {
      public List<CapturedMessage> Messages { get; } = new();

      public override void Write(string? message)
      {
        // Not used by Trace.WriteLine(message, category).
      }

      public override void WriteLine(string? message)
      {
        // Not used by Trace.WriteLine(message, category).
      }

      public override void WriteLine(string? message, string? category)
      {
        Messages.Add(new CapturedMessage(message, category));
      }

      public sealed record CapturedMessage(string? Message, string? Category);
    }
  }
}
