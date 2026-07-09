#nullable enable
using System;

namespace FluentDocker.Model.Models
{
  public sealed partial class ModelReference
  {
    /// <inheritdoc />
    public bool Equals(ModelReference? other)
    {
      if (other is null)
        return false;
      if (ReferenceEquals(this, other))
        return true;

      return string.Equals(Registry, other.Registry, StringComparison.OrdinalIgnoreCase)
          && string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
          && string.Equals(Name, other.Name, StringComparison.Ordinal)
          && string.Equals(Tag, other.Tag, StringComparison.Ordinal)
          && string.Equals(Digest, other.Digest, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ModelReference);

    /// <inheritdoc />
    public override int GetHashCode()
    {
      var hash = new HashCode();
      hash.Add(Registry, StringComparer.OrdinalIgnoreCase);
      hash.Add(Namespace, StringComparer.Ordinal);
      hash.Add(Name, StringComparer.Ordinal);
      hash.Add(Tag, StringComparer.Ordinal);
      hash.Add(Digest, StringComparer.Ordinal);
      return hash.ToHashCode();
    }

    /// <summary>Value equality operator.</summary>
    public static bool operator ==(ModelReference? left, ModelReference? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Value inequality operator.</summary>
    public static bool operator !=(ModelReference? left, ModelReference? right) => !(left == right);
  }
}
