using System;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  [Trait("Category", "Unit")]
  public class DockerfileBuilderTests
  {
    [Fact]
    public async Task UseParent_AddsFromInstruction()
    {
      var dockerfile = await new DockerfileBuilder()
          .UseParent("alpine:latest")
          .ToDockerfileStringAsync();

      Assert.Contains("FROM alpine:latest", dockerfile);
    }

    [Fact]
    public async Task From_WithAsName_AddsMultiStageBuild()
    {
      var dockerfile = await new DockerfileBuilder()
          .From("node:18", "builder")
          .ToDockerfileStringAsync();

      Assert.Contains("FROM node:18 AS builder", dockerfile);
    }

    [Fact]
    public async Task Run_AddsRunInstructions()
    {
      var dockerfile = await new DockerfileBuilder()
          .UseParent("alpine")
          .Run("apk update", "apk add nodejs")
          .ToDockerfileStringAsync();

      Assert.Contains("FROM alpine", dockerfile);
      Assert.Contains("RUN apk update", dockerfile);
      Assert.Contains("RUN apk add nodejs", dockerfile);
    }

    [Fact]
    public async Task Copy_AddsCopyInstruction()
    {
      var dockerfile = await new DockerfileBuilder()
          .UseParent("node:18")
          .Copy("package.json", "/app/")
          .ToDockerfileStringAsync();

      // COPY can be in shell form or JSON array form
      Assert.Contains("COPY", dockerfile);
      Assert.Contains("package.json", dockerfile);
      Assert.Contains("/app/", dockerfile);
    }

    [Fact]
    public async Task Copy_RootedSource_CopiesIntoBuildContextAndRendersRelativePath()
    {
      Directory.CreateDirectory(".out");
      var source = Path.GetFullPath(Path.Combine(".out", "rooted-copy-source.txt"));
      await File.WriteAllTextAsync(source, "hello", TestContext.Current.CancellationToken);
      var workingFolder = Path.Combine(".out", "rooted-copy-build");

      var dockerfile = await new DockerfileBuilder()
          .WorkingFolder(workingFolder)
          .UseParent("alpine")
          .Copy(source, "/app/source.txt")
          .ToDockerfileStringAsync();

      Assert.True(File.Exists(Path.Combine(workingFolder, "rooted-copy-source.txt")));
      Assert.Contains(@"COPY [""rooted-copy-source.txt"", ""/app/source.txt""]", dockerfile);
      Assert.DoesNotContain(source, dockerfile);
    }

    [Fact]
    public async Task Copy_RelativeSourceWithDotDotCannotEscapeOwnedBuildContext()
    {
      Directory.CreateDirectory(".out");
      var testRoot = Path.GetFullPath(Path.Combine(".out", "copy-dotdot"));
      var workingFolder = Path.Combine(testRoot, "context");
      var sourceFile = Path.Combine(testRoot, "source.txt");
      Directory.CreateDirectory(testRoot);
      await File.WriteAllTextAsync(sourceFile, "safe", TestContext.Current.CancellationToken);
      var source = Path.Combine("..", new DirectoryInfo(Directory.GetCurrentDirectory()).Name,
          Path.GetRelativePath(Directory.GetCurrentDirectory(), sourceFile)).Replace('\\', '/');

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new DockerfileBuilder()
          .WorkingFolder(workingFolder)
          .UseParent("alpine")
          .Copy(source, "/app/source.txt")
          .ToDockerfileStringAsync(TestContext.Current.CancellationToken));

      Assert.Contains("escapes the build context", ex.Message);
    }

    [Fact]
    public async Task Copy_WindowsPaths_NormalizesSeparatorsInDockerfile()
    {
      var dockerfile = await new DockerfileBuilder()
          .UseParent("alpine")
          .Copy(@"src\app.dll", @"C:\app\app.dll")
          .ToDockerfileStringAsync();

      Assert.Contains(@"COPY [""src/app.dll"", ""C:/app/app.dll""]", dockerfile);
    }

    [Fact]
    public async Task UseWorkDir_AddsWorkdirInstruction()
    {
      var dockerfile = await new DockerfileBuilder()
          .UseParent("node:18")
          .UseWorkDir("/app")
          .ToDockerfileStringAsync();

      Assert.Contains("WORKDIR /app", dockerfile);
    }

    [Fact]
    public async Task ExposePorts_AddsExposeInstruction()
    {
      var dockerfile = await new DockerfileBuilder()
          .UseParent("nginx")
          .ExposePorts(80, 443)
          .ToDockerfileStringAsync();

      Assert.Contains("EXPOSE 80 443", dockerfile);
    }

    [Fact]
    public async Task ExposePorts_NullPorts_DoesNotThrow()
    {
      int[] ports = null!;
      var dockerfile = await new DockerfileBuilder()
          .UseParent("nginx")
          .ExposePorts(ports)
          .ToDockerfileStringAsync();

      Assert.Contains("EXPOSE", dockerfile);
    }

    [Fact]
    public async Task Environment_AddsEnvInstruction()
    {
      var dockerfile = await new DockerfileBuilder()
          .UseParent("node:18")
          .Environment("NODE_ENV=production", "PORT=3000")
          .ToDockerfileStringAsync();

      Assert.Contains("ENV", dockerfile);
      Assert.Contains("NODE_ENV", dockerfile);
    }

    [Fact]
    public async Task Command_AddsCmdInstruction()
    {
      var dockerfile = await new DockerfileBuilder()
          .UseParent("node:18")
          .Command("npm", "start")
          .ToDockerfileStringAsync();

      Assert.Contains("CMD", dockerfile);
      Assert.Contains("npm", dockerfile);
    }

    [Fact]
    public async Task Entrypoint_AddsEntrypointInstruction()
    {
      var dockerfile = await new DockerfileBuilder()
          .UseParent("python:3.9")
          .Entrypoint("python", "-u", "app.py")
          .ToDockerfileStringAsync();

      Assert.Contains("ENTRYPOINT", dockerfile);
      Assert.Contains("python", dockerfile);
    }

    [Fact]
    public async Task Label_AddsLabelInstruction()
    {
      var dockerfile = await new DockerfileBuilder()
          .UseParent("alpine")
          .Label("maintainer=test@example.com", "version=1.0")
          .ToDockerfileStringAsync();

      Assert.Contains("LABEL", dockerfile);
      Assert.Contains("maintainer", dockerfile);
    }

    [Fact]
    public async Task User_AddsUserInstruction()
    {
      var dockerfile = await new DockerfileBuilder()
          .UseParent("alpine")
          .User("node")
          .ToDockerfileStringAsync();

      Assert.Contains("USER node", dockerfile);
    }

    [Fact]
    public async Task Volume_AddsVolumeInstruction()
    {
      var dockerfile = await new DockerfileBuilder()
          .UseParent("postgres")
          .Volume("/var/lib/postgresql/data")
          .ToDockerfileStringAsync();

      Assert.Contains("VOLUME [\"/var/lib/postgresql/data\"]", dockerfile);
    }

    [Fact]
    public async Task Command_ExecForm_EscapesQuotesAndBackslashes()
    {
      var dockerfile = await new DockerfileBuilder()
          .UseParent("alpine")
          .Command("echo", "a \"quoted\" value", @"C:\tools")
          .ToDockerfileStringAsync();

      Assert.Contains(@"CMD [""echo"", ""a \""quoted\"" value"", ""C:\\tools""]", dockerfile);
    }

    [Fact]
    public async Task EntrypointAndShell_ExecForm_EscapeQuotesAndBackslashes()
    {
      var dockerfile = await new DockerfileBuilder()
          .UseParent("alpine")
          .Entrypoint("dotnet", @"C:\app\main.dll")
          .Shell("powershell", "-Command", "Write-Host \"hi\"")
          .ToDockerfileStringAsync();

      Assert.Contains(@"ENTRYPOINT [""dotnet"", ""C:\\app\\main.dll""]", dockerfile);
      Assert.Contains(@"SHELL [""powershell"", ""-Command"", ""Write-Host \""hi\""""]", dockerfile);
    }

    [Fact]
    public async Task Add_WithSpaceAndQuote_UsesEscapedExecForm()
    {
      var dockerfile = await new DockerfileBuilder()
          .UseParent("alpine")
          .Add("folder with \"quote.txt", "/app/folder with \"quote.txt")
          .ToDockerfileStringAsync();

      Assert.Contains(
          @"ADD [""folder with \""quote.txt"", ""/app/folder with \""quote.txt""]",
          dockerfile);
    }

    [Fact]
    public async Task Add_WindowsPaths_NormalizesSeparatorsInDockerfile()
    {
      var dockerfile = await new DockerfileBuilder()
          .UseParent("alpine")
          .Add(@"conf\app.json", "/app/")
          .ToDockerfileStringAsync();

      Assert.Contains(@"ADD [""conf/app.json"", ""/app/""]", dockerfile);
    }

    [Fact]
    public async Task WithHealthCheck_AddsHealthcheckInstruction()
    {
      var dockerfile = await new DockerfileBuilder()
          .UseParent("nginx")
          .WithHealthCheck("curl -f http://localhost/ || exit 1", "30s", "10s")
          .ToDockerfileStringAsync();

      Assert.Contains("HEALTHCHECK", dockerfile);
      Assert.Contains("curl", dockerfile);
    }

    [Fact]
    public async Task Shell_AddsShellInstruction()
    {
      var dockerfile = await new DockerfileBuilder()
          .UseParent("mcr.microsoft.com/windows/servercore")
          .Shell("powershell", "-Command")
          .ToDockerfileStringAsync();

      Assert.Contains("SHELL", dockerfile);
      Assert.Contains("powershell", dockerfile);
    }

    [Fact]
    public async Task Arguments_AddsArgInstruction()
    {
      var dockerfile = await new DockerfileBuilder()
          .UseParent("alpine")
          .Arguments("VERSION", "1.0.0")
          .ToDockerfileStringAsync();

      Assert.Contains("ARG VERSION", dockerfile);
    }

    [Fact]
    public async Task FromString_UsesProvidedDockerfile()
    {
      var customDockerfile = "FROM alpine\nRUN echo hello";
      var dockerfile = await new DockerfileBuilder()
          .FromString(customDockerfile)
          .ToDockerfileStringAsync();

      Assert.Equal(customDockerfile, dockerfile);
    }

    [Fact]
    public async Task ComplexDockerfile_PreservesOrder()
    {
      var dockerfile = await new DockerfileBuilder()
          .UseParent("node:18")
          .UseWorkDir("/app")
          .Copy("package*.json", "./")
          .Run("npm ci")
          .Copy("src", ".")
          .ExposePorts(3000)
          .Command("node", "server.js")
          .ToDockerfileStringAsync();

      var lines = dockerfile.Split('\n', StringSplitOptions.RemoveEmptyEntries);

      // Verify order
      Assert.StartsWith("FROM", lines[0]);
      Assert.Contains("WORKDIR", lines[1]);
    }

    [Fact]
    public void ToDockerfileString_Synchronous_Works()
    {
      var dockerfile = new DockerfileBuilder()
          .UseParent("alpine")
          .Run("echo hello")
          .ToDockerfileString();

      Assert.Contains("FROM alpine", dockerfile);
      Assert.Contains("RUN echo hello", dockerfile);
    }

    [Fact]
    public async Task BuildAsync_WithoutParent_ThrowsException()
    {
      var builder = new DockerfileBuilder();

      await Assert.ThrowsAsync<FluentDocker.Common.FluentDockerException>(
          async () => await builder.BuildAsync());
    }

    [Fact]
    public async Task Copy_WithDirectorySource_ThrowsNotSupported()
    {
      var sourceDir = Path.Combine(".out", "dockerfile-dir-source");
      Directory.CreateDirectory(sourceDir);

      var ex = await Assert.ThrowsAsync<NotSupportedException>(() => new DockerfileBuilder()
          .UseParent("alpine")
          .Copy(sourceDir, "/app")
          .ToDockerfileStringAsync());

      Assert.Contains("Directory sources are not supported", ex.Message);
    }

    [Fact]
    public async Task Add_WithAbsoluteFileSource_CopiesOnEveryBuildWithoutMutatingSource()
    {
      var root = Path.GetFullPath(Path.Combine(".out", "dockerfile-add-rebuild"));
      var source = Path.Combine(root, "source.txt");
      var first = Path.Combine(root, "first");
      var second = Path.Combine(root, "second");
      Directory.CreateDirectory(root);
      await File.WriteAllTextAsync(source, "data", TestContext.Current.CancellationToken);
      var builder = new DockerfileBuilder()
          .WorkingFolder(first)
          .UseParent("alpine")
          .Add(source, "/data/source.txt");

      await builder.ToDockerfileStringAsync();
      builder.WorkingFolder(second);
      await builder.ToDockerfileStringAsync();

      Assert.True(File.Exists(Path.Combine(first, "source.txt")));
      Assert.True(File.Exists(Path.Combine(second, "source.txt")));
    }

    [Fact]
    public async Task CopyRelativeRootLevel_AddRootedSameBasenameDifferentContent_ThrowsCollision()
    {
      // Bare (no subdirectory) relative COPY source: must sit directly in the process CWD to
      // land at the context root, so it can collide with a rooted ADD's basename.
      var relativeName = $"fd-root-collision-{Guid.NewGuid():N}.conf";
      await File.WriteAllTextAsync(relativeName, "copy-bytes", TestContext.Current.CancellationToken);
      Directory.CreateDirectory(".out");
      var rootedDir = Path.GetFullPath(Path.Combine(".out", "add-rooted-collision-source"));
      Directory.CreateDirectory(rootedDir);
      var rootedSource = Path.Combine(rootedDir, relativeName);
      await File.WriteAllTextAsync(rootedSource, "add-bytes", TestContext.Current.CancellationToken);
      var workingFolder = Path.Combine(".out", "add-rooted-collision-build");

      try
      {
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => new DockerfileBuilder()
            .WorkingFolder(workingFolder)
            .UseParent("alpine")
            .Copy(relativeName, "/etc/a.conf")
            .Add(rootedSource, "/etc/b.conf")
            .ToDockerfileStringAsync(TestContext.Current.CancellationToken));

        Assert.Contains(relativeName, ex.Message);
      }
      finally
      {
        File.Delete(relativeName);
      }
    }

    [Fact]
    public async Task Add_RootedSourceRestagedIntoFixedWorkingFolder_RefreshesStaleContent()
    {
      Directory.CreateDirectory(".out");
      var root = Path.GetFullPath(Path.Combine(".out", "add-stale-refresh"));
      Directory.CreateDirectory(root);
      var source = Path.Combine(root, "app.conf");
      await File.WriteAllTextAsync(source, "v1", TestContext.Current.CancellationToken);
      var workingFolder = Path.Combine(root, "context");
      var builder = new DockerfileBuilder()
          .WorkingFolder(workingFolder)
          .UseParent("alpine")
          .Add(source, "/etc/app.conf");

      await builder.ToDockerfileStringAsync(TestContext.Current.CancellationToken);
      Assert.Equal("v1", await File.ReadAllTextAsync(
          Path.Combine(workingFolder, "app.conf"), TestContext.Current.CancellationToken));

      await File.WriteAllTextAsync(source, "v2", TestContext.Current.CancellationToken);
      await builder.ToDockerfileStringAsync(TestContext.Current.CancellationToken);

      Assert.Equal("v2", await File.ReadAllTextAsync(
          Path.Combine(workingFolder, "app.conf"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Add_SameRootedSourceUsedTwice_NoThrowAndCorrectBytes()
    {
      Directory.CreateDirectory(".out");
      var root = Path.GetFullPath(Path.Combine(".out", "add-same-source-twice"));
      Directory.CreateDirectory(root);
      var source = Path.Combine(root, "shared.conf");
      await File.WriteAllTextAsync(source, "shared-bytes", TestContext.Current.CancellationToken);
      var workingFolder = Path.Combine(root, "context");

      var dockerfile = await new DockerfileBuilder()
          .WorkingFolder(workingFolder)
          .UseParent("alpine")
          .Add(source, "/etc/a.conf")
          .Add(source, "/etc/b.conf")
          .ToDockerfileStringAsync(TestContext.Current.CancellationToken);

      Assert.Contains(@"ADD [""shared.conf"", ""/etc/a.conf""]", dockerfile);
      Assert.Contains(@"ADD [""shared.conf"", ""/etc/b.conf""]", dockerfile);
      Assert.Equal("shared-bytes", await File.ReadAllTextAsync(
          Path.Combine(workingFolder, "shared.conf"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ToImage_WithoutParent_ThrowsException()
    {
      var builder = new DockerfileBuilder();

      Assert.Throws<FluentDocker.Common.FluentDockerException>(
          () => builder.ToImage());
    }
  }
}
