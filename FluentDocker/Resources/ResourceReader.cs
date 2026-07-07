#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FluentDocker.Common;

namespace FluentDocker.Resources
{
  public sealed class ResourceReader(IEnumerable<ResourceInfo> resources) : IEnumerable<ResourceStream>
  {
    private readonly ResourceInfo[] _resources = [.. resources];

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

      internal ResourceStreamEnumerator(ResourceInfo[] resources) => _resources = resources;

      public void Dispose()
      {
        _pos = -2;
        GC.SuppressFinalize(this);
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
          var name = $"{res.Namespace}.{res.Resource}";
          var stream = res.Assembly.GetManifestResourceStream(name)
            ?? throw new FluentDockerException(
              $"Manifest resource '{name}' was not found in assembly '{res.Assembly.GetName().Name}'.");
          return new ResourceStream(stream, res);
        }
      }

      object IEnumerator.Current => Current;
    }
  }
}
