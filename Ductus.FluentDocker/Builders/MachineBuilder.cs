using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Builders
{
  public sealed class MachineBuilder : BaseBuilder<IHostService>
  {
    private readonly HostBuilderConfig _config = new HostBuilderConfig();

    internal MachineBuilder(IBuilder parent) : base(parent)
    {
    }

    public override IHostService Build()
    {
      var machine = new Hosts().FromMachineName(_config.Name);
      if (null != machine)
      {
        if (machine.State != ServiceRunningState.Running)
        {
          machine.Start();
        }

        return machine;
      }

      var resp = _config.Name.Create(_config.MemoryMb, _config.StorageSizeMb, _config.CpuCount);
      if (!resp.Success)
      {
        throw new FluentDockerException($"Could not create machine {_config.Name} Log: {resp}");
      }

      return Build();
    }

    protected override IBuilder InternalCreate()
    {
      return new MachineBuilder(this);
    }

    public MachineBuilder UseDriver(string driver)
    {
      _config.Driver = driver;
      return this;
    }

    public MachineBuilder WithName(string machineName)
    {
      _config.Name = machineName;
      return this;
    }

    public MachineBuilder CpuCount(int numCpus)
    {
      _config.CpuCount = numCpus;
      return this;
    }

    public MachineBuilder Memory(int memoryMb)
    {
      _config.MemoryMb = memoryMb;
      return this;
    }

    public MachineBuilder StorageSize(int storageMb)
    {
      _config.StorageSizeMb = storageMb;
      return this;
    }

    public HostBuilder Host()
    {
      return (HostBuilder)((IBuilder)this).Parent.Value;
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
  }
}
