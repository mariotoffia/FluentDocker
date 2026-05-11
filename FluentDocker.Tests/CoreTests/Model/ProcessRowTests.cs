using System;
using System.Collections.Generic;
using System.Reflection;
using FluentDocker.Model.Containers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  [Trait("Category", "Unit")]
  public class ProcessRowTests
  {
    // ProcessRow.ToRow is internal. Invoke via reflection.
    private static ProcessRow ToRow(IList<string> columns, IList<string> values)
    {
      var method = typeof(ProcessRow).GetMethod(
          "ToRow",
          BindingFlags.NonPublic | BindingFlags.Static);
      return (ProcessRow)method.Invoke(null, [columns, values]);
    }

    [Fact]
    public void ToRow_StandardHmsForm_ParsesStartedAndTime()
    {
      var row = ToRow(["START", "TIME"], ["00:00:12", "00:01:30"]);

      Assert.Equal(TimeSpan.FromSeconds(12), row.Started);
      Assert.Equal(TimeSpan.FromSeconds(90), row.Time);
    }

    [Theory]
    [InlineData("0s", 0)]
    [InlineData("12s", 12)]
    [InlineData("59s", 59)]
    public void ToRow_PodmanSecondsForm_ParsesStarted(string value, int expectedSeconds)
    {
      var row = ToRow(["START"], [value]);

      Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), row.Started);
    }

    [Theory]
    [InlineData("0m0s", 0, 0)]
    [InlineData("12m34s", 12, 34)]
    [InlineData("5m9s", 5, 9)]
    public void ToRow_PodmanMinutesSecondsForm_ParsesTime(string value, int minutes, int seconds)
    {
      var row = ToRow(["TIME"], [value]);

      Assert.Equal(new TimeSpan(0, minutes, seconds), row.Time);
    }

    [Theory]
    [InlineData("0h0m0s", 0, 0, 0)]
    [InlineData("1h2m3s", 1, 2, 3)]
    [InlineData("12h34m56s", 12, 34, 56)]
    public void ToRow_PodmanHoursMinutesSecondsForm_ParsesStarted(string value, int hours, int minutes, int seconds)
    {
      var row = ToRow(["START"], [value]);

      Assert.Equal(new TimeSpan(hours, minutes, seconds), row.Started);
    }

    [Fact]
    public void ToRow_PodmanCpuColumnAcceptsSecondsForm()
    {
      var row = ToRow(["CPU"], ["7s"]);

      Assert.Equal(TimeSpan.FromSeconds(7), row.Cpu);
    }

    [Fact]
    public void ToRow_CpuColumnGarbageInput_LeavesCpuAtDefault()
    {
      var row = ToRow(["CPU"], ["not-a-duration"]);

      Assert.Equal(TimeSpan.Zero, row.Cpu);
    }

    [Fact]
    public void ToRow_StartedColumnGarbage_Throws()
    {
      // Started uses the throwing form (Parse). A genuinely malformed value
      // should still surface as an exception — we don't want to silently
      // mask future container-engine quirks by widening the contract.
      var ex = Assert.Throws<TargetInvocationException>(() => ToRow(["START"], ["totally-garbage"]));
      Assert.IsType<FormatException>(ex.InnerException);
    }
  }
}
