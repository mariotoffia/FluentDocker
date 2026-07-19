#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using FluentDocker.Common;

namespace FluentDocker.Resources
{
  /// <summary>
  /// Lazily opens a <see cref="ResourceStream"/> for each <see cref="ResourceInfo"/> in
  /// <paramref name="resources"/> as it is enumerated, via
  /// <see cref="System.Reflection.Assembly.GetManifestResourceStream(string)"/>.
  /// Each yielded <see cref="ResourceStream"/> should be disposed by the consumer once read.
  /// </summary>
  /// <param name="resources">The resources to open streams for, in enumeration order.</param>
  public sealed class ResourceReader(IEnumerable<ResourceInfo> resources) : IEnumerable<ResourceStream>
  {
    private readonly ResourceInfo[] _resources = [.. resources];

    /// <summary>Returns an enumerator that opens one <see cref="ResourceStream"/> per resource on demand.</summary>
    public IEnumerator<ResourceStream> GetEnumerator()
    {
      return new ResourceStreamEnumerator(_resources);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
      return GetEnumerator();
    }

    // A hand-rolled enumerator (not a compiler iterator) because Reset() is part of this
    // class's supported surface; use after Dispose() throws ObjectDisposedException instead
    // of violating the enumerator contract (MoveNext true + Current IndexOutOfRangeException).
    private sealed class ResourceStreamEnumerator : IEnumerator<ResourceStream>
    {
      private readonly ResourceInfo[] _resources;
      private int _pos = -1;
      private bool _disposed;

      internal ResourceStreamEnumerator(ResourceInfo[] resources) => _resources = resources;

      public void Dispose()
      {
        _disposed = true;
      }

      public bool MoveNext()
      {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pos < _resources.Length)
          _pos++;
        return _pos < _resources.Length;
      }

      public void Reset()
      {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pos = -1;
      }

      public ResourceStream Current
      {
        get
        {
          ObjectDisposedException.ThrowIf(_disposed, this);
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
