using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
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

      Assert.Contains("VOLUME", dockerfile);
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
          .Copy(".", ".")
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
    public void BuildAsync_WithoutParent_ThrowsException()
    {
      var builder = new DockerfileBuilder();

      Assert.ThrowsAsync<FluentDocker.Common.FluentDockerException>(
          async () => await builder.BuildAsync());
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

