using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Shared fixture harness for the Docker CLI production-readiness suites. Each derived suite
  /// builds a throwaway POSIX shell script that stands in for the <c>docker</c> binary, optionally
  /// recording the arguments it receives, and resolves it through a stub
  /// <see cref="IBinaryResolver"/>. Centralising the plumbing keeps every driver-focused suite free
  /// of copy-pasted fake-docker boilerplate.
  /// </summary>
  public abstract class DockerCliFakeDockerTestBase
  {
    /// <summary>Writes <paramref name="script"/> to a unique executable file and returns its path.</summary>
    protected static string CreateFakeDocker(string script)
    {
      var directory = TestOutputDirectory();
      var path = Path.Combine(directory, $"fake-docker-{Guid.NewGuid():N}");
      File.WriteAllText(path, script.Replace("\r\n", "\n"));
      using var chmod = Process.Start(new ProcessStartInfo
      {
        FileName = "chmod",
        UseShellExecute = false,
        CreateNoWindow = true,
        ArgumentList = { "+x", path }
      });
      chmod!.WaitForExit();
      return path;
    }

    /// <summary>
    /// Creates a fake docker that records its arguments (one per line) to <paramref name="record"/>
    /// and echoes <paramref name="output"/> on stdout.
    /// </summary>
    protected static string CreateRecordingDocker(string record, string output)
    {
      return CreateFakeDocker($$"""
#!/bin/sh
printf '%s\n' "$@" > "{{record}}"
echo "{{output}}"
exit 0
""");
    }

    /// <summary>Reads the argument lines recorded by <see cref="CreateRecordingDocker"/>.</summary>
    protected static async Task<string[]> ReadArgsAsync(string record)
    {
      var text = await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken);
      return text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>The per-run scratch directory the fake binaries and their markers are written to.</summary>
    protected static string TestOutputDirectory()
    {
      var directory = Path.Combine(Directory.GetCurrentDirectory(), ".out", "docker-cli-production-tests");
      Directory.CreateDirectory(directory);
      return directory;
    }

    /// <summary>Waits on a generous wall-clock ceiling for a fake-process marker file to appear.</summary>
    protected static Task WaitForFileAsync(string path)
        => FakeProcessMarker.WaitForFileAsync(path, TestContext.Current.CancellationToken);

    /// <summary>Resolves every docker command to a single fixed fake binary path.</summary>
    protected sealed class FakeResolver : IBinaryResolver
    {
      private readonly DockerBinary _binary;

      public FakeResolver(string path) =>
        _binary = new DockerBinary(
            Path.GetDirectoryName(path) ?? ".",
            Path.GetFileName(path),
            SudoMechanism.None,
            null!,
            DockerBinaryType.DockerClient);

      public DockerBinary[] Binaries => [_binary];
      public DockerBinary MainDockerClient => _binary;
      public DockerBinary MainDockerCli => _binary;
      public DockerBinary Resolve(string binary) => _binary;
      public string ResolveBinaryPath(string dockerCommand) => _binary.FqPath;
    }
  }
}
