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
      return _useNative ? new Hosts().Discover().First(x => x.IsNative) : null;
    }

    protected override IBuilder InternalCreate()
    {
      return new HostBuilder(this);
    }


    public HostBuilder UseNative()
    {
      _useNative = true;
      return this;
    }

    public MachineBuilder UseMachine()
    {
      var builder = new MachineBuilder(this);
      Childs.Add(builder);
      return builder;
    }

    public ContainerBuilder UseContainer()
    {
      return new ContainerBuilder(this);
    }
  }
}