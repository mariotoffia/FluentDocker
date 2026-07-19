using System;
using System.Linq;
using System.Text;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// Exception thrown when a test resource fails to initialize after diagnostics
  /// have been collected.
  /// </summary>
  public sealed class ResourceInitializationException : Exception
  {
    /// <summary>
    /// Diagnostics captured before the failed resource was cleaned up.
    /// </summary>
    public ResourceDiagnostics? Diagnostics { get; }

    /// <summary>
    /// Creates an initialization exception with diagnostics and the original
    /// failure as <see cref="Exception.InnerException"/>.
    /// </summary>
    public ResourceInitializationException(
        string message,
        ResourceDiagnostics? diagnostics,
        Exception innerException)
        : base(message, innerException)
        => Diagnostics = diagnostics;

    /// <inheritdoc />
    public override string ToString()
    {
      var text = base.ToString();
      var diagnostics = FormatDiagnostics(Diagnostics);
      return string.IsNullOrEmpty(diagnostics)
          ? text
          : text + Environment.NewLine + Environment.NewLine + diagnostics;
    }

    private static string FormatDiagnostics(ResourceDiagnostics? diagnostics)
    {
      if (diagnostics == null)
        return string.Empty;

      var builder = new StringBuilder();
      builder.AppendLine("Resource diagnostics:");
      AppendIfPresent(builder, "Resource", diagnostics.ResourceName);
      AppendIfPresent(builder, "Driver", diagnostics.DriverId);
      AppendIfPresent(builder, "Operation", diagnostics.OperationContext);
      AppendIfPresent(builder, "Failure", diagnostics.Failure?.Message);
      AppendBlock(builder, "Logs", Truncate(diagnostics.Logs));
      AppendBlock(builder, "Inspect", Truncate(diagnostics.InspectPayload));
      return builder.ToString().TrimEnd();
    }

    private static void AppendIfPresent(StringBuilder builder, string name, string? value)
    {
      if (!string.IsNullOrWhiteSpace(value))
        builder.Append(name).Append(": ").AppendLine(value);
    }

    private static void AppendBlock(StringBuilder builder, string name, string? value)
    {
      if (!string.IsNullOrWhiteSpace(value))
        builder.Append(name).AppendLine(":").AppendLine(value);
    }

    private static string? Truncate(string? value)
    {
      const int maxLines = 200;
      const int maxChars = 12000;
      if (string.IsNullOrEmpty(value))
        return value;

      var lines = value.Split('\n');
      var truncated = lines.Length > maxLines
          ? string.Join('\n', lines.Take(maxLines)) +
            $"\n... ({lines.Length - maxLines} lines truncated)"
          : value;
      return truncated.Length <= maxChars
          ? truncated
          : truncated[..maxChars] + $"... ({truncated.Length - maxChars} chars truncated)";
    }
  }
}
