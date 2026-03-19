namespace FluentDocker.Model.Drivers
{
  /// <summary>
  /// Represents a void return type for CommandResponse when no data is returned.
  /// Similar to F# unit type or void in C#.
  /// </summary>
  public struct Unit
  {
    /// <summary>
    /// Default instance of Unit.
    /// </summary>
    public static readonly Unit Default;

    /// <summary>
    /// Returns a string representation of this Unit.
    /// </summary>
    public override string ToString() => "()";

    /// <summary>
    /// Determines whether this Unit equals another object.
    /// </summary>
    public override bool Equals(object obj) => obj is Unit;

    /// <summary>
    /// Returns the hash code for this Unit.
    /// </summary>
    public override int GetHashCode() => 0;

    public static bool operator ==(Unit left, Unit right) => true;
    public static bool operator !=(Unit left, Unit right) => false;
  }
}
