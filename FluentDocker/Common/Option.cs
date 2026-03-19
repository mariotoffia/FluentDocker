namespace FluentDocker.Common
{
  /// <summary>
  /// Optional value wrapper. Represents a value that may or may not be present (Some/None pattern).
  /// Works with both reference and value types.
  /// </summary>
  /// <typeparam name="T">The value type.</typeparam>
#pragma warning disable CA1716 // Type name 'Option' conflicts with reserved keyword — intentional API design
#pragma warning disable CA1000 // Static members on generic type — factory pattern is intentional API design
  public sealed class Option<T>
  {
    /// <summary>Creates an option wrapping the given value. Null produces a None option for reference types.</summary>
    public Option(T value)
    {
      Value = value;
      HasValue = value is not null;
    }

    private Option()
    {
      Value = default!;
      HasValue = false;
    }

    /// <summary>Creates a None option with no value.</summary>
    public static Option<T> None() => new();

    /// <summary>The wrapped value, or default if None.</summary>
    public T Value { get; }

    /// <summary>True if a value is present (Some); false otherwise (None).</summary>
    public bool HasValue { get; }

    /// <summary>Implicitly unwraps the option to its value.</summary>
    public static implicit operator T(Option<T> option) => option.Value;

    /// <summary>Explicitly wraps a value in an option.</summary>
    public static explicit operator Option<T>(T value) => new Option<T>(value);
  }
#pragma warning restore CA1000
#pragma warning restore CA1716
}
