using System.Collections.Generic;

namespace Ductus.FluentDocker.Model.Containers
{
  public sealed class ContainerConfig
  {
    public string Hostname { get; set; }
    public string Domainname { get; set; }
    public string User { get; set; }
    public bool AttachStdin { get; set; }
    public bool AttachStdout { get; set; }
    public bool AttachStderr { get; set; }
    public IDictionary<string /*port/proto*/, object> ExposedPorts { get; set; }
    public bool Tty { get; set; }
    public bool OpenStdin { get; set; }
    public bool StdinOnce { get; set; }
    public string[] Env { get; set; }
    public string[] Cmd { get; set; }
    public string Image { get; set; }
    public IDictionary<string, VolumeMount> Volumes { get; set; }
    public string WorkingDir { get; set; }
    public string[] EntryPoint { get; set; }
    public IDictionary<string, string> Labels { get; set; }
    public string StopSignal { get; set; }
  }
}