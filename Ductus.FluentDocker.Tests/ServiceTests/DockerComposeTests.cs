using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Compose;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Impl;
using Ductus.FluentDocker.Tests.Extensions;
using Ductus.FluentDocker.Services.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BindingFlags = System.Reflection.BindingFlags;

// ReSharper disable StringLiteralTypo

namespace Ductus.FluentDocker.Tests.ServiceTests
{
  [TestClass]
  public class DockerComposeTests : FluentDockerTestBase
  {
    [TestMethod]
    public async Task WordPressDockerComposeServiceShallShowInstallScreen()
    {
      var file = Path.Combine(Directory.GetCurrentDirectory(),
        (TemplateString)"Resources/ComposeTests/WordPress/docker-compose.yml");

      using (var svc = new DockerComposeCompositeService(DockerHost, new DockerComposeConfig
      {
        ComposeFilePath = new List<string> { file },
        ForceRecreate = true,
        RemoveOrphans = true,
        StopOnDispose = true
      }))
      {
        svc.Start();

        svc.Containers.First(x => x.Name == "wordpress").WaitForHttp("http://localhost:8000/wp-admin/install.php");

        // We now have a running WordPress with a MySql database
        var installPage = await $"http://localhost:8000/wp-admin/install.php".Wget();

        Assert.IsTrue(installPage.IndexOf("https://wordpress.org/", StringComparison.Ordinal) != -1);
      }
    }

    [TestMethod]
    [DataRow(new[] { "non-existens-docker-compose1.yml" })]
    [DataRow(new[] { "non-existens-docker-compose1.yml", "non-existens-docker-compose2.yml" })]
    public void StartErrorMessageShallContainDockerComposeFilePaths(string[] nonExistentComposeFiles)
    {
      var svc = new DockerComposeCompositeService(DockerHost, new DockerComposeConfig
      {
        ComposeFilePath = new List<string>(nonExistentComposeFiles),
        ForceRecreate = true,
        RemoveOrphans = true,
        StopOnDispose = true
      });

      var ex = Assert.ThrowsException<FluentDockerException>(() => svc.Start());

      foreach (var nonExistentComposeFile in nonExistentComposeFiles)
      {
        Assert.IsTrue(ex.Message.Contains(nonExistentComposeFile));
      }
    }

    [TestMethod]
    [DataRow(new[] { "non-existens-docker-compose1.yml" })]
    [DataRow(new[] { "non-existens-docker-compose1.yml", "non-existens-docker-compose2.yml" })]
    public void DisposeErrorMessageShallContainDockerComposeFilePaths(string[] nonExistentComposeFiles)
    {
      var svc = new DockerComposeCompositeService(DockerHost, new DockerComposeConfig
      {
        ComposeFilePath = new List<string>(nonExistentComposeFiles),
        ForceRecreate = true,
        RemoveOrphans = true,
        StopOnDispose = true
      });

      // ReSharper disable once AccessToDisposedClosure
      var ex = Assert.ThrowsException<FluentDockerException>(() => svc.Dispose());

      foreach (var nonExistentComposeFile in nonExistentComposeFiles)
      {
        Assert.IsTrue(ex.Message.Contains(nonExistentComposeFile));
      }
    }

    [TestMethod]
    [DataRow(new[] { "docker-compose1.yml" })]
    [DataRow(new[] { "docker-compose1.yml", "docker-compose2.yml" })]
    public void CompositeBuilderBuildErrorNoHostFoundShallContainComposeFilePaths(string[] composeFiles)
    {
      var builder = ((CompositeBuilder)typeof(CompositeBuilder)
          .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
          .First()
          .Invoke(new object[] {new BuilderFake(), null}))
        .FromFile(composeFiles);

      var ex = Assert.ThrowsException<FluentDockerException>(() => builder.Build());

      foreach (var nonExistentComposeFile in composeFiles)
      {
        Assert.IsTrue(ex.Message.Contains(nonExistentComposeFile));
      }
    }

    private class BuilderFake : IBuilder
    {
      /// <inheritdoc />
      public Option<IBuilder> Parent { get; } = new Option<IBuilder>(null);

      /// <inheritdoc />
      public Option<IBuilder> Root { get; } = new Option<IBuilder>(null);

      /// <inheritdoc />
      public IReadOnlyCollection<IBuilder> Children { get; } = new ReadOnlyCollection<IBuilder>(new List<IBuilder>());

      /// <inheritdoc />
      public IBuilder Create() => null;

      /// <inheritdoc />
      public IService Build() => null;
    }
  }
}
