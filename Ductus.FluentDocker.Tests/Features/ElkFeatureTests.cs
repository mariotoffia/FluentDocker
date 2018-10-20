using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model;
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
      feature.Initialize();
      feature.Execute();

      IsTrue(Directory.Exists(feature.Target));
      IsTrue(Directory.GetFiles(feature.Target).Any(x => x.EndsWith("docker-compose.yml")));

      feature.Dispose();
      IsFalse(Directory.Exists(feature.Target));
    }

    [TestMethod]
    public void KeepOnDisposeWillKeepClonedRepo()
    {
      var feature = new ElkFeature();
      feature.Initialize(new Dictionary<string, string>() {{FeatureConstants.KeepOnDispose, "true"}});
      feature.Execute();      
      feature.Dispose();
      
      IsTrue(Directory.Exists(feature.Target));
      IsTrue(Directory.GetFiles(feature.Target).Any(x => x.EndsWith("docker-compose.yml")));

      CleanFolder(Path.GetFileName(feature.Target));
      IsFalse(Directory.Exists(feature.Target));      
    }

    [TestMethod]
    public void IfNoTargetPathIsSetARandomIsChosen()
    {
      var feature = new ElkFeature();
      feature.Initialize();
      feature.Execute();
      
      IsTrue(Directory.Exists(feature.Target));
      IsTrue(Directory.GetFiles(feature.Target).Any(x => x.EndsWith("docker-compose.yml")));
      
      CleanFolder(Path.GetFileName(feature.Target));
    }

    [TestMethod]
    public void ExecuteFeatureWillCloneElkRepository()
    {
      var path = CleanFolder("repo-here");
      
      var feature = new ElkFeature();
      feature.Initialize(new Dictionary<string, string>() {{ElkFeature.TargetPath, "repo-here"}});
      feature.Execute();
      
      IsTrue(Directory.Exists(path));
      IsTrue(Directory.GetFiles(path).Any(x => x.EndsWith("docker-compose.yml")));
      
      CleanFolder("repo-here");
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