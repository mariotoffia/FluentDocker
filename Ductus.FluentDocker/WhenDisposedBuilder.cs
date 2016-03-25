using System.Linq;
using Ductus.FluentDocker.Internal;

namespace Ductus.FluentDocker
{
  public class WhenDisposedBuilder
  {
    private readonly DockerBuilder _builder;
    private readonly DockerParams _prms;

    internal WhenDisposedBuilder(DockerBuilder builder, DockerParams prms)
    {
      _builder = builder;
      _prms = prms;
    }

    public WhenDisposedBuilder RemoveVolume(params string[] directory)
    {
      if (null == directory || 0 == directory.Length)
      {
        _prms.VolumesToRemoveOnDispose = null;
        return this;
      }

      _prms.VolumesToRemoveOnDispose = directory.Select(dir => dir.Render()).ToArray();
      return this;
    }

    public WhenDisposedBuilder KeepContainer()
    {
      _prms.RemoveContainerOnDispose = true;
      return this;
    }

    public WhenDisposedBuilder KeepContainerRunning()
    {
      _prms.StopContainerOnDispose = false;
      _prms.RemoveContainerOnDispose = false;
      return this;
    }

    public DockerBuilder ConfigureContainer()
    {
      return _builder;
    }

    public DockerContainer Build(bool startImmediately = false)
    {
      return _builder.Build(startImmediately);
    }
  }
}