using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model;
using Ductus.FluentDocker.Services;
using FluentDocker.Features.Elk;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Ductus.FluentDocker.Tests.Features
{
  [TestClass]
  public sealed class ElkFeatureTests
  {
    [TestMethod]
    public void DisposeDeletesAllContent()
    {
      var feature = new ElkFeature();
      try
      {
        feature.Initialize();
        feature.Execute();

        IsTrue(Directory.Exists(feature.Target));
        IsTrue(Directory.GetFiles(feature.Target).Any(x => x.EndsWith("docker-compose.yml")));

        feature.Dispose();
        IsFalse(Directory.Exists(feature.Target));
      }
      catch
      {
        feature.Dispose();
        throw;
      }
    }

    [TestMethod]
    public void KeepOnDisposeWillKeepClonedRepo()
    {
      var feature = new ElkFeature();
      try
      {
        feature.Initialize(new Dictionary<string, object> {{FeatureConstants.KeepOnDispose, "true"}});
        feature.Execute();
        feature.Dispose();

        IsTrue(Directory.Exists(feature.Target));
        IsTrue(Directory.GetFiles(feature.Target).Any(x => x.EndsWith("docker-compose.yml")));

        feature.Services.OfType<ICompositeService>().First().Dispose();
        CleanFolder(Path.GetFileName(feature.Target));
        IsFalse(Directory.Exists(feature.Target));
      }
      catch
      {
        try
        {
          feature.Services.OfType<ICompositeService>().First().Dispose();
        } catch { /*ignore*/}

        CleanFolder(Path.GetFileName(feature.Target));
        throw;
      }
    }

    [TestMethod]
    public void IfNoTargetPathIsSetARandomIsChosen()
    {
      var feature = new ElkFeature();
      try
      {
        feature.Initialize();
        feature.Execute();

        IsTrue(Directory.Exists(feature.Target));
        IsTrue(Directory.GetFiles(feature.Target).Any(x => x.EndsWith("docker-compose.yml")));

        feature.Dispose();
        CleanFolder(Path.GetFileName(feature.Target));
      }
      catch
      {
        feature.Dispose();
        CleanFolder(Path.GetFileName(feature.Target));
        throw;
      }
    }

    [TestMethod]
    public void ExecuteFeatureWillCloneElkRepository()
    {
      var path = CleanFolder("repo-here");

      var feature = new ElkFeature();
      try
      {
        feature.Initialize(new Dictionary<string, object> {{ElkFeature.TargetPath, "repo-here"}});
        feature.Execute();

        IsTrue(Directory.Exists(path));
        IsTrue(Directory.GetFiles(path).Any(x => x.EndsWith("docker-compose.yml")));

        feature.Dispose();
        CleanFolder("repo-here");
      }
      catch
      {
        feature.Dispose();
        CleanFolder("repo-here");
        throw;
      }
    }

    [TestMethod]
    public void EnableCuratorExtensionShallWork()
    {
      try
      {
        using (var feature = new ElkFeature())
        {
          feature.Initialize(new Dictionary<string, object> {{ElkFeature.EnableCurator, "true"}});
          feature.Execute();

          var curatorService = feature.Services.OfType<ICompositeService>().First().Services.FirstOrDefault(x => x.Name == "curator");
          IsNotNull(curatorService);
        }
      }
      catch
      {
        Console.WriteLine("Just to make sure Dispose is invoked on feature");
        throw;
      }
    }

    [TestMethod]
    public void EnableApmExtensionShallWork()
    {
      try
      {
        using (var feature = new ElkFeature())
        {
          feature.Initialize(new Dictionary<string, object> {{ElkFeature.EnableApmServer, "true"}});
          feature.Execute();

          var apmServer = feature.Services.OfType<ICompositeService>().First().Services.FirstOrDefault(x => x.Name == "apm-server");
          IsNotNull(apmServer);
        }
      }
      catch
      {
        Console.WriteLine("Just to make sure Dispose is invoked on feature");
        throw;
      }
    }
    
    [TestMethod]
    [Ignore] // Since not windows compatible
    public void EnableLogSpoutExtensionShallWork()
    {
      try
      {
        using (var feature = new ElkFeature())
        {
          feature.Initialize(new Dictionary<string, object> {{ElkFeature.EnableLogspout, "true"}});
          feature.Execute();

          var logspout = feature.Services.OfType<ICompositeService>().First().Services.FirstOrDefault(x => x.Name == "logspout");
          IsNotNull(logspout);
        }
      }
      catch
      {
        Console.WriteLine("Just to make sure Dispose is invoked on feature");
        throw;
      }
    }

    private static string CleanFolder(string relativePath)
    {
      var path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
      try
      {
        if (Directory.Exists(path)) DirectoryHelper.DeleteDirectory(path);
      }
      catch (Exception e)
      {
        Console.WriteLine($"Got error while deleting {path} msg = {e.Message}");
      }

      return path;
    }
  }
}