using System;
using System.IO;
using System.Net;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model;

namespace Ductus.FluentDocker.Services.Impl
{
  public sealed class DockerContainerService : ServiceBase, IContainerService
  {
    private readonly CertificatePaths _certificates;
    private readonly bool _removeOnDispose;
    private readonly bool _stopOnDispose;
    private Container _containerConfigCache;

    public DockerContainerService(string name, string id, Uri docker, ServiceRunningState state,
      CertificatePaths certificates,
      bool stopOnDispose = true, bool removeOnDispose = true) : base(name)
    {
      _certificates = certificates;
      _stopOnDispose = stopOnDispose;
      _removeOnDispose = removeOnDispose;

      Id = id;
      DockerHost = docker;
      State = state;
    }

    public string Id { get; }
    public Uri DockerHost { get; }

    public Container GetConfiguration(bool fresh = false)
    {
      if (!fresh && null != _containerConfigCache)
      {
        return _containerConfigCache;
      }

      _containerConfigCache = DockerHost.InspectContainer(Id, _certificates).Data;
      return _containerConfigCache;
    }

    public IPEndPoint ToHosExposedtPort(string portAndProto)
    {
      return GetConfiguration()?.NetworkSettings.Ports.ToHostPort(portAndProto, DockerHost);
    }

    public Processes GetRunningProcesses()
    {
      return DockerHost.Top(Id, _certificates).Data;
    }

    public string Export(TemplateString fqPath, bool explode = false)
    {
      string path = explode ? Path.GetTempFileName() : fqPath;
      var result = DockerHost.Export(Id, path, _certificates);
      if (!result.Success)
      {
        return null;
      }

      if (!explode)
      {
        return path;
      }

      try
      {
        path.UnTar(fqPath);
      }
      finally
      {
        File.Delete(path);
      }

      return fqPath;
    }

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
      State = ServiceRunningState.Starting;
      DockerHost.Start(Id, _certificates);
      if (GetConfiguration().State.Running)
      {
        State = ServiceRunningState.Running;
      }
    }

    public override void Stop()
    {
      State = ServiceRunningState.Stopping;
      var res = DockerHost.Stop(Id, null, _certificates);
      if (res.Success)
      {
        State = ServiceRunningState.Stopped;
      }
    }

    public override void Remove(bool force = false)
    {
      if (State != ServiceRunningState.Stopped)
      {
        State = ServiceRunningState.Stopping;
      }

      var result = DockerHost.RemoveContainer(Id, force, false, null, _certificates);
      if (result.Success)
      {
        State = ServiceRunningState.Stopped;
        State = ServiceRunningState.Removed;
      }
    }
  }
}