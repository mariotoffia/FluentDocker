using System.Collections.Generic;
using System.Linq;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
    /// <summary>
    /// Unit tests for <c>docker compose top</c> text table output parsing.
    /// The <see cref="DockerCliComposeDriver.ParseTopOutput"/> method splits
    /// the CLI output into blocks per container, extracts column headers,
    /// and maps each process row into a dictionary.
    /// </summary>
    [Trait("Category", "Unit")]
    public class DockerCliComposeTopParsingTests
    {
        #region Multi-Service Output

        [Fact]
        public void ParseTopOutput_MultiService_ParsesBothContainers()
        {
            var output = string.Join("\n", new[]
            {
                "my-project-web-1",
                "UID    PID     PPID    C    STIME   TTY   TIME     CMD",
                "root   12345   12344   0    10:30   ?     00:00:01 nginx -g daemon off;",
                "root   12346   12345   0    10:30   ?     00:00:00 nginx: worker process",
                "",
                "my-project-db-1",
                "UID    PID     PPID    C    STIME   TTY   TIME     CMD",
                "999    12400   12399   0    10:30   ?     00:00:05 mysqld"
            });

            var result = DockerCliComposeDriver.ParseTopOutput(output);

            Assert.Equal(2, result.Count);

            // First container: web
            Assert.Equal("my-project-web-1", result[0].Service);
            Assert.Equal("my-project-web-1", result[0].ContainerId);
            Assert.Equal(2, result[0].Processes.Count);

            // Second container: db
            Assert.Equal("my-project-db-1", result[1].Service);
            Assert.Equal("my-project-db-1", result[1].ContainerId);
            Assert.Single(result[1].Processes);
        }

        [Fact]
        public void ParseTopOutput_MultiService_HeadersMappedCorrectly()
        {
            var output = string.Join("\n", new[]
            {
                "my-project-web-1",
                "UID    PID     PPID    C    STIME   TTY   TIME     CMD",
                "root   12345   12344   0    10:30   ?     00:00:01 nginx -g daemon off;",
                "",
                "my-project-db-1",
                "UID    PID     PPID    C    STIME   TTY   TIME     CMD",
                "999    12400   12399   0    10:30   ?     00:00:05 mysqld"
            });

            var result = DockerCliComposeDriver.ParseTopOutput(output);

            var webProc = result[0].Processes[0];
            Assert.Equal("root", webProc["UID"]);
            Assert.Equal("12345", webProc["PID"]);
            Assert.Equal("12344", webProc["PPID"]);
            Assert.Equal("0", webProc["C"]);
            Assert.Equal("10:30", webProc["STIME"]);
            Assert.Equal("?", webProc["TTY"]);
            Assert.Equal("00:00:01", webProc["TIME"]);

            var dbProc = result[1].Processes[0];
            Assert.Equal("999", dbProc["UID"]);
            Assert.Equal("12400", dbProc["PID"]);
            Assert.Equal("mysqld", dbProc["CMD"]);
        }

        [Fact]
        public void ParseTopOutput_MultiService_LastColumnPreservesSpaces()
        {
            var output = string.Join("\n", new[]
            {
                "my-project-web-1",
                "UID    PID     PPID    C    STIME   TTY   TIME     CMD",
                "root   12345   12344   0    10:30   ?     00:00:01 nginx -g daemon off;",
                "root   12346   12345   0    10:30   ?     00:00:00 nginx: worker process",
                "",
                "my-project-db-1",
                "UID    PID     PPID    C    STIME   TTY   TIME     CMD",
                "999    12400   12399   0    10:30   ?     00:00:05 mysqld --defaults-file=/etc/mysql/my.cnf"
            });

            var result = DockerCliComposeDriver.ParseTopOutput(output);

            // CMD column should contain the full command with spaces
            Assert.Equal("nginx -g daemon off;", result[0].Processes[0]["CMD"]);
            Assert.Equal("nginx: worker process", result[0].Processes[1]["CMD"]);
            Assert.Equal(
                "mysqld --defaults-file=/etc/mysql/my.cnf",
                result[1].Processes[0]["CMD"]);
        }

        #endregion

        #region Single Service Output

        [Fact]
        public void ParseTopOutput_SingleService_ParsesCorrectly()
        {
            var output = string.Join("\n", new[]
            {
                "my-project-app-1",
                "UID    PID     PPID    C    STIME   TTY   TIME     CMD",
                "root   5000    4999    0    08:00   ?     00:00:10 python app.py --host 0.0.0.0"
            });

            var result = DockerCliComposeDriver.ParseTopOutput(output);

            Assert.Single(result);
            Assert.Equal("my-project-app-1", result[0].Service);
            Assert.Equal("my-project-app-1", result[0].ContainerId);
            Assert.Single(result[0].Processes);

            var proc = result[0].Processes[0];
            Assert.Equal("root", proc["UID"]);
            Assert.Equal("5000", proc["PID"]);
            Assert.Equal("python app.py --host 0.0.0.0", proc["CMD"]);
        }

        [Fact]
        public void ParseTopOutput_SingleServiceMultipleProcesses_AllParsed()
        {
            var output = string.Join("\n", new[]
            {
                "my-project-web-1",
                "UID    PID     PPID    C    STIME   TTY   TIME     CMD",
                "root   100     99      0    09:00   ?     00:00:01 apache2 -DFOREGROUND",
                "www    101     100     0    09:00   ?     00:00:00 apache2 -DFOREGROUND",
                "www    102     100     0    09:00   ?     00:00:00 apache2 -DFOREGROUND",
                "www    103     100     0    09:00   ?     00:00:00 apache2 -DFOREGROUND"
            });

            var result = DockerCliComposeDriver.ParseTopOutput(output);

            Assert.Single(result);
            Assert.Equal(4, result[0].Processes.Count);
            Assert.Equal("root", result[0].Processes[0]["UID"]);
            Assert.Equal("www", result[0].Processes[1]["UID"]);
            Assert.Equal("www", result[0].Processes[2]["UID"]);
            Assert.Equal("www", result[0].Processes[3]["UID"]);
        }

        [Fact]
        public void ParseTopOutput_SingleService_NoProcessRows_EmptyProcesses()
        {
            var output = string.Join("\n", new[]
            {
                "my-project-idle-1",
                "UID    PID     PPID    C    STIME   TTY   TIME     CMD"
            });

            var result = DockerCliComposeDriver.ParseTopOutput(output);

            Assert.Single(result);
            Assert.Equal("my-project-idle-1", result[0].Service);
            Assert.Empty(result[0].Processes);
        }

        #endregion

        #region Empty / Whitespace Output

        [Fact]
        public void ParseTopOutput_EmptyString_ReturnsEmptyList()
        {
            var result = DockerCliComposeDriver.ParseTopOutput("");

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseTopOutput_Null_ReturnsEmptyList()
        {
            var result = DockerCliComposeDriver.ParseTopOutput(null);

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseTopOutput_WhitespaceOnly_ReturnsEmptyList()
        {
            var result = DockerCliComposeDriver.ParseTopOutput("   \n  \n   ");

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void ParseTopOutput_WindowsLineEndings_ParsesCorrectly()
        {
            var output = "my-project-web-1\r\n" +
                         "UID    PID     CMD\r\n" +
                         "root   1234    nginx\r\n" +
                         "\r\n" +
                         "my-project-db-1\r\n" +
                         "UID    PID     CMD\r\n" +
                         "999    5678    postgres\r\n";

            var result = DockerCliComposeDriver.ParseTopOutput(output);

            Assert.Equal(2, result.Count);
            Assert.Equal("my-project-web-1", result[0].Service);
            Assert.Equal("root", result[0].Processes[0]["UID"]);
            Assert.Equal("nginx", result[0].Processes[0]["CMD"]);
            Assert.Equal("my-project-db-1", result[1].Service);
            Assert.Equal("postgres", result[1].Processes[0]["CMD"]);
        }

        [Fact]
        public void ParseTopOutput_TrailingNewlines_DoesNotCreateExtraEntries()
        {
            var output = string.Join("\n", new[]
            {
                "my-project-web-1",
                "UID    PID     CMD",
                "root   1234    nginx",
                "",
                "",
                ""
            });

            var result = DockerCliComposeDriver.ParseTopOutput(output);

            Assert.Single(result);
            Assert.Equal("my-project-web-1", result[0].Service);
        }

        [Fact]
        public void ParseTopOutput_MultipleBlankLinesBetweenBlocks_StillSplitsCorrectly()
        {
            var output = string.Join("\n", new[]
            {
                "container-a",
                "UID    PID     CMD",
                "root   100     process-a",
                "",
                "",
                "",
                "container-b",
                "UID    PID     CMD",
                "root   200     process-b"
            });

            var result = DockerCliComposeDriver.ParseTopOutput(output);

            Assert.Equal(2, result.Count);
            Assert.Equal("container-a", result[0].Service);
            Assert.Equal("container-b", result[1].Service);
        }

        [Fact]
        public void ParseTopOutput_SingleLineBlock_Skipped()
        {
            // A block with only one line (just a container name, no headers)
            // should be skipped since it has no useful data.
            var output = "orphaned-container-1";

            var result = DockerCliComposeDriver.ParseTopOutput(output);

            Assert.Empty(result);
        }

        [Fact]
        public void ParseTopOutput_DifferentColumnSets_ParsedPerBlock()
        {
            // Different containers might have different column sets
            var output = string.Join("\n", new[]
            {
                "container-a",
                "UID    PID     CMD",
                "root   100     my-app",
                "",
                "container-b",
                "USER   PID     PPID    COMMAND",
                "app    200     199     java -jar server.jar"
            });

            var result = DockerCliComposeDriver.ParseTopOutput(output);

            Assert.Equal(2, result.Count);

            // First block has UID, PID, CMD headers
            var procA = result[0].Processes[0];
            Assert.Equal("root", procA["UID"]);
            Assert.Equal("100", procA["PID"]);
            Assert.Equal("my-app", procA["CMD"]);

            // Second block has USER, PID, PPID, COMMAND headers
            var procB = result[1].Processes[0];
            Assert.Equal("app", procB["USER"]);
            Assert.Equal("200", procB["PID"]);
            Assert.Equal("199", procB["PPID"]);
            Assert.Equal("java -jar server.jar", procB["COMMAND"]);
        }

        [Fact]
        public void ParseTopOutput_CommandWithMultipleSpaces_PreservedInLastColumn()
        {
            var output = string.Join("\n", new[]
            {
                "my-container",
                "UID    PID     CMD",
                "root   1       /bin/sh -c echo hello world && sleep infinity"
            });

            var result = DockerCliComposeDriver.ParseTopOutput(output);

            Assert.Single(result);
            var proc = result[0].Processes[0];
            Assert.Equal(
                "/bin/sh -c echo hello world && sleep infinity",
                proc["CMD"]);
        }

        #endregion
    }
}
