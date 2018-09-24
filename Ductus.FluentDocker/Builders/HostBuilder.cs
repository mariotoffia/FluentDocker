using System.Linq;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Builders
{
  public sealed class HostBuilder : BaseBuilder<IHostService>
  {
    private bool _useNative;

    internal HostBuilder(IBuilder builder) : base(builder)
    {
    }

    public override IHostService Build()
    {
      return _useNative ? new Hosts().Discover(_useNative).First(x => x.IsNative) : null;
    }

    protected override IBuilder InternalCreate()
    {
      return new HostBuilder(this);
    }

    public bool IsNative => _useNative;

    public HostBuilder UseNative()
    {
      _useNative = true;
      return this;
    }

    public MachineBuilder UseMachine()
    {
      var existing = Childs.FirstOrDefault(x => x is MachineBuilder);
      if (null != existing)
      {
        return (MachineBuilder) existing;
      }

      var builder = new MachineBuilder(this);
      Childs.Add(builder);
      return builder;
    }

    public RemoteSshHostBuilder UseSsh(string url)
    {
      var builder = new RemoteSshHostBuilder(this);
      Childs.Add(builder);
      return builder;
    }

    public ImageBuilder DefineImage(string image = null)
    {
      var builder = new ImageBuilder(this).AsImageName(image);
      Childs.Add(builder);
      return builder;
    }

    public ContainerBuilder UseContainer()
    {
      var builder = new ContainerBuilder(this);
      Childs.Add(builder);
      return builder;
    }

    public NetworkBuilder UseNetwork(string name = null)
    {      
      var builder = new NetworkBuilder(this, name);
      Childs.Add(builder);
      return builder;
    }

    public VolumeBuilder UseVolume(string name = null)
    {
      var builder = new VolumeBuilder(this, name);
      Childs.Add(builder);
      return builder;
    }
  }
}