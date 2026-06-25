using System.Reflection;
using FluentDocker.Common;

namespace FluentDocker.Model.Models.Inference
{
  /// <summary>
  /// Internal helper that produces an independent copy of an inference request DTO via a
  /// JSON round-trip and writes every public settable property onto a target instance.
  /// <para>
  /// Using a round-trip (rather than a hand-maintained per-field copy) guarantees that any
  /// future-added request property is preserved automatically, and that collection properties
  /// are deep-copied instead of sharing the source's list references. The reflection-based
  /// write means no property can be silently dropped — adding a property requires no change here.
  /// </para>
  /// </summary>
  internal static class InferenceDtoCopy
  {
    /// <summary>
    /// Copies all public, settable properties of <paramref name="source"/> into
    /// <paramref name="target"/> using a JSON round-trip for independence (deep copy).
    /// </summary>
    /// <typeparam name="T">The DTO type (must be STJ-(de)serializable).</typeparam>
    /// <param name="source">The instance to copy from.</param>
    /// <param name="target">The instance to copy onto (typically <c>this</c> in a copy ctor).</param>
    public static void CopyInto<T>(T source, T target) where T : class
    {
      // Round-trip yields a fresh object graph (independent lists) honoring STJ annotations.
      var clone = JsonHelper.TryDeserialize<T>(JsonHelper.Serialize(source));
      if (clone is null)
        return;

      foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
      {
        if (prop.CanRead && prop.CanWrite)
          prop.SetValue(target, prop.GetValue(clone));
      }
    }
  }
}
