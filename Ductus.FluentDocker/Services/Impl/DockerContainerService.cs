using System;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Model;

namespace Ductus.FluentDocker.Services.Impl
{
  public sealed class DockerContainerService : ServiceBase, IContainerService
  {
    private readonly CertificatePaths _certificates;
    private readonly bool _removeOnDispose;
    private readonly bool _stopOnDispose;

    public DockerContainerService(string name, string id, Uri docker, CertificatePaths certificates,
      bool stopOnDispose = false, bool removeOnDispose = false) : base(name)
    {
      _certificates = certificates;
      _stopOnDispose = stopOnDispose;
      _removeOnDispose = removeOnDispose;

      Id = id;
      DockerHost = docker;
    }

    public string Id { get; }
    public Uri DockerHost { get; }

    public override void Dispose()
    {
      if (string.IsNullOrEmpty(Id) || null == DockerHost)
      {
        return;
      }

      if (_stopOnDispose)
      {
        DockerHost.Stop(Id, null, _certificates);
      }

      if (_removeOnDispose)
      {
        DockerHost.RemoveContainer(Id, false, true, null, _certificates);
      }
    }

    public override void Start()
    {
    }
  }
}