using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Builders
{
  public sealed class ContainerBuilder : BaseBuilder<IContainerService>
  {
    internal ContainerBuilder(IBuilder parent) : base(parent)
    {
    }

    public override IContainerService Build()
    {
      throw new System.NotImplementedException();
    }

    protected override IBuilder InternalCreate()
    {
      return new ContainerBuilder(this);
    }
  }
}
