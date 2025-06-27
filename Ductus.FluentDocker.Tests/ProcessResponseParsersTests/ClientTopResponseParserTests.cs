using System;
using System.Linq;
using System.Reflection;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Executors.Parsers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.ProcessResponseParsersTests
{
  [TestClass]
  public class ClientTopResponseParserTests
  {
    [TestMethod]
    public void ProcessShallParseResponse()
    {
      // Arrange
      var stdOut =
        @"UID                 PID                 PPID                C                   STIME               TTY                 TIME                CMD
999                 7765                7735                0                   20:23               ?                   00:00:00            postgres
999                 7838                7765                0                   20:23               ?                   00:00:00            postgres: checkpointer
999                 7839                7765                0                   20:23               ?                   00:00:00            postgres: background writer
999                 7841                7765                0                   20:23               ?                   00:00:00            postgres: walwriter
999                 7842                7765                0                   20:23               ?                   00:00:00            postgres: autovacuum launcher
999                 7843                7765                0                   20:23               ?                   00:00:00            postgres: logical replication launcher";
      var ctorArgs = new object[] { "command", stdOut, "", 0 };
      var executionResult = (ProcessExecutionResult)Activator.CreateInstance(typeof(ProcessExecutionResult),
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.CreateInstance,
        null, ctorArgs, null, null);

      var parser = new ClientTopResponseParser();

      // Act
      var result = parser.Process(executionResult);

      // Assert
      Assert.IsTrue(result.Response.Success);
      var processes = result.Response.Data;
      Assert.IsTrue(processes.Rows.All(row => row.User == "999"));
      Assert.IsTrue(processes.Rows.All(row => row.Started == new TimeSpan(20, 23, 0)));
      Assert.IsTrue(processes.Rows.All(row => row.Time == TimeSpan.Zero));
      Assert.IsTrue(processes.Rows.All(row => row.Tty == "?"));
      Assert.AreEqual(7765, processes.Rows[0].Pid);
      Assert.AreEqual(7838, processes.Rows[1].Pid);
      Assert.AreEqual(7839, processes.Rows[2].Pid);
      Assert.AreEqual(7841, processes.Rows[3].Pid);
      Assert.AreEqual(7842, processes.Rows[4].Pid);
      Assert.AreEqual(7843, processes.Rows[5].Pid);

      Assert.AreEqual(7735, processes.Rows[0].ProcessPid);
      Assert.AreEqual(7765, processes.Rows[1].ProcessPid);
      Assert.AreEqual(7765, processes.Rows[2].ProcessPid);
      Assert.AreEqual(7765, processes.Rows[3].ProcessPid);
      Assert.AreEqual(7765, processes.Rows[4].ProcessPid);
      Assert.AreEqual(7765, processes.Rows[5].ProcessPid);

      Assert.AreEqual("postgres", processes.Rows[0].Command);
      Assert.AreEqual("postgres: checkpointer", processes.Rows[1].Command);
      Assert.AreEqual("postgres: background writer", processes.Rows[2].Command);
      Assert.AreEqual("postgres: walwriter", processes.Rows[3].Command);
      Assert.AreEqual("postgres: autovacuum launcher", processes.Rows[4].Command);
      Assert.AreEqual("postgres: logical replication launcher", processes.Rows[5].Command);
    }


    [TestMethod]
    public void ProcessShallParsePodmanOutput()
    {
      // Arrange
      var stdOut =
        @"USER        PID         PPID        %CPU        ELAPSED            TTY         TIME        COMMAND
postgres    1           0           0.000       12h0m5.863267473s  ?           0s          postgres
postgres    55          1           0.000       12h0m4.863350723s  ?           0s          postgres: checkpointer
postgres    56          1           0.000       12h0m4.863378515s  ?           0s          postgres: background writer
postgres    58          1           0.000       12h0m4.863404598s  ?           0s          postgres: walwriter
postgres    59          1           0.000       12h0m4.86343039s   ?           0s          postgres: autovacuum launcher
postgres    60          1           0.000       12h0m4.863453973s  ?           0s          postgres: logical replication launcher";
      var ctorArgs = new object[] { "command", stdOut, "", 0 };
      var executionResult = (ProcessExecutionResult)Activator.CreateInstance(typeof(ProcessExecutionResult),
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.CreateInstance,
        null, ctorArgs, null, null);

      // Act
      var parser = new ClientTopResponseParser();
      var result = parser.Process(executionResult);

      // Assert
      Assert.IsTrue(result.Response.Success);
      var processes = result.Response.Data;
      Assert.IsTrue(processes.Rows.All(row => row.User == "postgres"));
      Assert.IsTrue(processes.Rows.All(row => row.PercentCpuUtilization == 0f));
      Assert.IsTrue(processes.Rows.All(row => row.Tty == "?"));
      Assert.IsTrue(processes.Rows.All(row => row.Time == TimeSpan.Zero));
      Assert.AreEqual(1, processes.Rows[0].Pid);
      Assert.AreEqual(55, processes.Rows[1].Pid);
      Assert.AreEqual(56, processes.Rows[2].Pid);
      Assert.AreEqual(58, processes.Rows[3].Pid);
      Assert.AreEqual(59, processes.Rows[4].Pid);
      Assert.AreEqual(60, processes.Rows[5].Pid);

      Assert.AreEqual(0, processes.Rows[0].ProcessPid);
      Assert.AreEqual(1, processes.Rows[1].ProcessPid);
      Assert.AreEqual(1, processes.Rows[2].ProcessPid);
      Assert.AreEqual(1, processes.Rows[3].ProcessPid);
      Assert.AreEqual(1, processes.Rows[4].ProcessPid);
      Assert.AreEqual(1, processes.Rows[5].ProcessPid);

      Assert.AreEqual("postgres", processes.Rows[0].Command);
      Assert.AreEqual("postgres: checkpointer", processes.Rows[1].Command);
      Assert.AreEqual("postgres: background writer", processes.Rows[2].Command);
      Assert.AreEqual("postgres: walwriter", processes.Rows[3].Command);
      Assert.AreEqual("postgres: autovacuum launcher", processes.Rows[4].Command);
      Assert.AreEqual("postgres: logical replication launcher", processes.Rows[5].Command);
    }
  }
}
