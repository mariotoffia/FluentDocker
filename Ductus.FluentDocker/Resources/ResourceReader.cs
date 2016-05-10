using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Ductus.FluentDocker.Resources
{
  public sealed class ResourceReader : IEnumerable<ResourceStream>
  {
    private readonly ResourceInfo[] _resources;

    public ResourceReader(IEnumerable<ResourceInfo> resources)
    {
      _resources = resources.ToArray();
    }

    public IEnumerator<ResourceStream> GetEnumerator()
    {
      return new ResourceStreamEnumerator(_resources);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
      return GetEnumerator();
    }

    private sealed class ResourceStreamEnumerator : IEnumerator<ResourceStream>
    {
      private readonly ResourceInfo[] _resources;
      private int _pos = -1;

      internal ResourceStreamEnumerator(ResourceInfo[] resources)
      {
        _resources = resources;
      }

      public void Dispose()
      {
        _pos = -2;
      }

      public bool MoveNext()
      {
        return ++_pos < _resources.Length;
      }

      public void Reset()
      {
        _pos = -1;
      }

      public ResourceStream Current
      {
        get
        {
          var res = _resources[_pos];
          return new ResourceStream(res.Assembly.GetManifestResourceStream($"{res.Namespace}.{res.Resource}"), res);
        }
      }

      object IEnumerator.Current => Current;
    }
  }
}