using System;
using System.Reflection;
using FluentDocker.Executors;
using FluentDocker.Executors.Parsers;
using FluentDocker.Model.Containers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Parser
{
  [Trait("Category", "Unit")]
  public class ClientTopResponseParserTests
  {
    [Fact]
    public void Parse_LinuxOutput_Returns4Columns()
    {
      var stdOut =
        "UID                 PID                 PPID                CMD\n" +
        "root                1234                5678                /bin/sh";
      var result = Parse(stdOut);

      Assert.True(result.Success);
      Assert.Equal(4, result.Data.Columns.Count);
      Assert.Equal("UID", result.Data.Columns[0]);
      Assert.Equal("CMD", result.Data.Columns[3]);
      Assert.Single(result.Data.Rows);
      Assert.Equal("root", result.Data.Rows[0].User);
      Assert.Equal(1234, result.Data.Rows[0].Pid);
    }

    [Fact]
    public void Parse_WindowsOutput_MergesMultiWordHeader()
    {
      // "Private Working Set" is 3 words → ParseColumns sees 6 starts → merged to 4
      var stdOut =
        "Name                PID                 CPU                 Private Working Set\n" +
        "smss.exe            320                 00:00:00.046        532480";
      var result = Parse(stdOut);

      Assert.True(result.Success);
      Assert.Equal(4, result.Data.Columns.Count);
      Assert.Equal("Name", result.Data.Columns[0]);
      Assert.Equal("PID", result.Data.Columns[1]);
      Assert.Equal("CPU", result.Data.Columns[2]);
      Assert.Equal("Private Working Set", result.Data.Columns[3]);
      Assert.Single(result.Data.Rows);
      Assert.Equal("smss.exe", result.Data.Rows[0].Command);
    }

    [Fact]
    public void Parse_NonEnglishWindowsOutput_StillMerges6Columns()
    {
      // German locale: "Privater Arbeitssatz Satz" → 3 words → 6 starts → merged to 4
      var stdOut =
        "Name                PID                 CPU                 Privater Arbeitssatz Satz\n" +
        "smss.exe            320                 00:00:00.046        532480";
      var result = Parse(stdOut);

      Assert.True(result.Success);
      Assert.Equal(4, result.Data.Columns.Count);
      Assert.Equal("Privater Arbeitssatz Satz", result.Data.Columns[3]);
    }

    [Fact]
    public void Parse_5ColumnOutput_NotMerged()
    {
      var stdOut =
        "UID                 PID                 PPID                TTY                 CMD\n" +
        "root                1234                5678                pts/0               /bin/sh";
      var result = Parse(stdOut);

      Assert.True(result.Success);
      Assert.Equal(5, result.Data.Columns.Count);
      Assert.Equal("TTY", result.Data.Columns[3]);
      Assert.Equal("CMD", result.Data.Columns[4]);
    }

    [Fact]
    public void Parse_EmptyOutput_ReturnsError()
    {
      var executionResult = CreateProcessExecutionResult("docker top cid", "", "error", 1);
      var parser = new ClientTopResponseParser();

      parser.Process(executionResult);

      Assert.False(parser.Response.Success);
    }

    [Fact]
    public void Parse_HeaderOnly_ReturnsNoRows()
    {
      var stdOut = "UID                 PID                 PPID                CMD";
      var result = Parse(stdOut);

      Assert.True(result.Success);
      Assert.Equal(4, result.Data.Columns.Count);
      Assert.Empty(result.Data.Rows);
    }

    [Fact]
    public void Parse_MultipleRows_ReturnsAllRows()
    {
      var stdOut =
        "UID                 PID                 PPID                CMD\n" +
        "root                1                   0                   /sbin/init\n" +
        "root                100                 1                   /usr/sbin/sshd\n" +
        "www                 200                 100                 nginx";
      var result = Parse(stdOut);

      Assert.True(result.Success);
      Assert.Equal(3, result.Data.Rows.Count);
      Assert.Equal(1, result.Data.Rows[0].Pid);
      Assert.Equal(100, result.Data.Rows[1].Pid);
      Assert.Equal(200, result.Data.Rows[2].Pid);
    }

    private static CommandResponse<Processes> Parse(string stdOut)
    {
      var executionResult = CreateProcessExecutionResult("docker top cid", stdOut, "", 0);
      var parser = new ClientTopResponseParser();
      parser.Process(executionResult);
      return parser.Response;
    }

    /// <summary>
    /// Helper to create ProcessExecutionResult using reflection (constructor is internal).
    /// </summary>
    private static ProcessExecutionResult CreateProcessExecutionResult(
        string command, string stdOut, string stdErr, int exitCode)
    {
      var ctorArgs = new object[] { command, stdOut, stdErr, exitCode };
      var result = (ProcessExecutionResult)Activator.CreateInstance(
          typeof(ProcessExecutionResult),
          BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.CreateInstance,
          null, ctorArgs, null, null);

      return result;
    }
  }
}
