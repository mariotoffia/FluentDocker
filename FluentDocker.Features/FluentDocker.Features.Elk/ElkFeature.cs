using System;
using System.Collections.Generic;
using System.IO;
using Ductus.FluentDocker;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model;
using Ductus.FluentDocker.Model.Compose;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Impl;
using LibGit2Sharp;

namespace FluentDocker.Features.Elk
{
  /// <summary>
  ///   Enables ELK stack.
  /// </summary>
  /// <remarks>
  ///   Based on the https://github.com/deviantony/docker-elk.
  /// </remarks>
  [Feature(Id = FeatureId)]
  public class ElkFeature : IFeature
  {
    private const string FeatureId = "elk-feature/1.0.0";

    public const string TargetPath = "elk-feature.target.path";
    public const string SourceUrl = "elk-feature.source.url";
    private IHostService _host;
    private bool _keepOnDispose;
    private string _source;
    private ICompositeService _svc;

    public string Target { get; private set; }

    public void Dispose()
    {
      if (_keepOnDispose) return;

      foreach (var service in Services)
        if (service is IContainerService || service is ICompositeService)
          service.Dispose();

      DirectoryHelper.DeleteDirectory(Path.GetFileName(Target));

      Services = new IService[0];
    }

    public string Id { get; } = FeatureId;
    public IEnumerable<IService> Services { get; private set; } = new IService[0];

    public void Initialize(IDictionary<string, object> settings = null)
    {
      if (null == settings)
      {
        _host = Fd.Native();
        _source = "https://github.com/deviantony/docker-elk.git";
        Target = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString());
        return;
      }

      _source = settings.ContainsKey(SourceUrl)
        ? (string) settings[SourceUrl]
        : "https://github.com/deviantony/docker-elk.git";

      Target = !settings.ContainsKey(TargetPath) ? Guid.NewGuid().ToString() : (string) settings[TargetPath];
      _keepOnDispose = settings.ContainsKey(FeatureConstants.KeepOnDispose);

      _host = !settings.ContainsKey(FeatureConstants.HostService)
        ? Fd.Native()
        : (IHostService) settings[FeatureConstants.HostService];

      if (!Path.IsPathRooted(Target)) Target = Path.Combine(Directory.GetCurrentDirectory(), Target);
    }

    public void Execute(params string[] arguments)
    {
      Repository.Clone(_source, Target);

      var file = Path.Combine(Target, "docker-compose.yml");
      // TODO: later on when swarm is supported in Fd - "docker-stack.yml" can also be selected...

      _svc = new DockerComposeCompositeService(_host, new DockerComposeConfig
      {
        ComposeFilePath = new List<string> { file }, ForceRecreate = false, RemoveOrphans = false,
        StopOnDispose = true
      });

      _svc.Start();

      Services = new IService[] {_host, _svc};
    }
  }
}