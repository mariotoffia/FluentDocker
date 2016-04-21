using System.Collections.Generic;
using System.Collections.ObjectModel;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Builders
{
  public abstract class BaseBuilder<T> : IBuilder<T>
  {
    private readonly Option<IBuilder> _parent;
    protected readonly IList<IBuilder> Childs = new List<IBuilder>();

    protected BaseBuilder(IBuilder parent)
    {
      _parent = new Option<IBuilder>(parent);
    }

    Option<IBuilder> IBuilder.Parent => _parent;

    Option<IBuilder> IBuilder.Root
    {
      get
      {
        var root = new Option<IBuilder>(null);
        for (var p = ((IBuilder) this).Parent; p.HasValue; p = p.Value.Parent)
        {
          root = p;
        }

        return root;
      }
    }

    public IReadOnlyCollection<IBuilder> Children => new ReadOnlyCollection<IBuilder>(Childs);

    public abstract T Build();

    IBuilder IBuilder.Create()
    {
      var builder = InternalCreate();
      Childs.Add(builder);
      return builder;
    }

    IService IBuilder.Build()
    {
      return (IService) Build();
    }

    protected abstract IBuilder InternalCreate();
  }
}