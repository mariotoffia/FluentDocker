using System;
using System.Collections.Generic;
using System.IO;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model;
using LibGit2Sharp;

namespace FluentDocker.Features.Elk
{
  public class ElkFeature : IFeature
  {
    public const string TargetPath = "elk-feature.target.path";
    public const string SourceUrl = "elk-feature.source.url";
    private string _target;
    private string _source;
    private bool _keepOnDispose;
    public void Dispose()
    {
      if (!_keepOnDispose) DirectoryHelper.DeleteDirectory(Path.GetFileName(_target));
    }

    public string Target => _target;

    public string Id { get; } = "elk-feature/1.0.0";
    public void Initialize(IDictionary<string, string> settings = null)
    {
      if (null == settings)
      {
        _source = "https://github.com/deviantony/docker-elk.git";
        _target = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString());
        return;
      }

      _source = settings.ContainsKey(SourceUrl) ? settings[SourceUrl] : "https://github.com/deviantony/docker-elk.git";
      _target = !settings.ContainsKey(TargetPath) ? Guid.NewGuid().ToString() : settings[TargetPath];
      _keepOnDispose = settings.ContainsKey(FeatureConstants.KeepOnDispose);
      
      if (!Path.IsPathRooted(_target))
      {
        _target = Path.Combine(Directory.GetCurrentDirectory(), _target);
      }
    }

    public void Execute(params string[] arguments)
    {
      Repository.Clone(_source, _target);
    }
  }
}