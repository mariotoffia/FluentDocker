using System.Collections.Generic;
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

    public ContainerBuilder UseContainer()
    {
      return new ContainerBuilder(this);
    }

    private static void InternalBuild(IList<IService> services, IBuilder builder)
    {
      var service = builder.Build();
      if (null != service)
      {
        services.Add(service);
      }

      foreach (var child in builder.Children)
      {
        InternalBuild(services, child);
      }
    }
  }
}