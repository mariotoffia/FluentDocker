using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Impl;

namespace Ductus.FluentDocker.Builders
{
  public class Builder : BaseBuilder<ICompositeService>
  {
    public Builder(IBuilder parent = null) : base(parent)
    {
    }

    public override ICompositeService Build()
    {
      var list = new List<IService>();
      foreach (var child in Childs)
      {
        InternalBuild(list, child);
      }

      return new BuilderCompositeService(list, "built-service");
    }

    protected override IBuilder InternalCreate()
    {
      return new Builder(this);
    }

    public HostBuilder UseHost()
    {
      return new HostBuilder(this);
    }

    public ImageBuilder DefineImage(string image)
    {
      var hb = FindOrDefineHostBuilder();
      return hb.IsNative ? hb.DefineImage(image) : hb.UseMachine().DefineImage(image);
    }

    public ContainerBuilder UseContainer()
    {
      var hb = FindOrDefineHostBuilder();
      return hb.IsNative ? hb.UseContainer() : hb.UseMachine().UseContainer();
    }

    public NetworkBuilder UseNetwork(string name = null)
    {
      return FindOrDefineHostBuilder().UseNetwork(name);
    }

    public VolumeBuilder UseVolume(string name = null)
    {
      return FindOrDefineHostBuilder().UseVolume(name);
    }

    private HostBuilder FindOrDefineHostBuilder()
    {
      var existing = Childs.FirstOrDefault(x => x is HostBuilder);
      if (null != existing)
      {
        return (HostBuilder)existing;
      }

      var host = new HostBuilder(this);
      Childs.Add(host);

      if (null != new Hosts().Native())
      {
        return host.UseNative();
      }

      var h = new Hosts().Discover().FirstOrDefault();
      if (null == h)
      {
        throw new FluentDockerException("Cannot build a container when no host is defined");
      }

      var config = h.GetMachineConfiguration();
      if (null == config)
      {
        throw new FluentDockerException("Cannot build container since no machine configuration and no native is found");
      }

      return host.UseMachine()
        .CpuCount(config.CpuCount)
        .Memory(config.MemorySizeMb)
        .StorageSize(config.StorageSizeMb)
        .UseDriver(config.DriverName)
        .WithName(config.Name).Host();
    }

    private static void InternalBuild(IList<IService> services, IBuilder builder)
    {
      foreach (var child in builder.Children)
      {
        InternalBuild(services, child);
      }

      var service = builder.Build();
      if (null != service)
      {
        services.Add(service);
      }
    }
  }
}
