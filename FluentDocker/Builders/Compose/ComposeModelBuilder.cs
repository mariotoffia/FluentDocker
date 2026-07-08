using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FluentDocker.Common;
using FluentDocker.Model.Compose;

namespace FluentDocker.Builders.Compose
{
  /// <summary>
  /// Accumulates Compose <c>models:</c> entries and per-service bindings and emits
  /// a Compose overlay file (no YAML dependency — a focused hand-rolled emitter for
  /// the <c>models:</c> shape).
  /// </summary>
  public sealed class ComposeModelBuilder : IComposeModelBuilder
  {
    // Compose keys (service / model keys) and env-var names are validated as strict
    // identifiers; the free-form model reference is restricted to its legal charset.
    // This is the YAML-injection boundary: a newline/control character in any scalar
    // could otherwise inject arbitrary Compose entries into the emitted overlay.
    private static readonly Regex KeyPattern = new(@"^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.Compiled);
    private static readonly Regex ModelReferencePattern = new(@"^[A-Za-z0-9][A-Za-z0-9._/:@+-]*$", RegexOptions.Compiled);

    private readonly List<ComposeModelSpec> _models = [];
    private readonly List<ComposeServiceModelBinding> _bindings = [];

    /// <inheritdoc />
    public IComposeModelBuilder AddModel(string key, Action<IComposeModelSpecBuilder> spec)
    {
      ArgumentNullException.ThrowIfNull(key);
      ArgumentNullException.ThrowIfNull(spec);

      var builder = new SpecBuilder(key);
      spec(builder);
      _models.Add(builder.Build());
      return this;
    }

    /// <inheritdoc />
    public IComposeModelBuilder BindToService(string service, string modelKey, string endpointVar = null, string modelVar = null)
    {
      ArgumentNullException.ThrowIfNull(service);
      ArgumentNullException.ThrowIfNull(modelKey);

      _bindings.Add(new ComposeServiceModelBinding
      {
        Service = service,
        ModelKey = modelKey,
        EndpointVar = endpointVar,
        ModelVar = modelVar
      });
      return this;
    }

    /// <summary>Renders the Compose overlay (top-level <c>models:</c> + per-service <c>models:</c>).</summary>
    /// <returns>The overlay YAML.</returns>
    public string EmitOverlay()
    {
      var sb = new StringBuilder();

      if (_bindings.Count > 0)
      {
        sb.Append("services:\n");
        foreach (var group in _bindings.GroupBy(b => b.Service))
        {
          sb.Append("  ").Append(ValidateKey(group.Key, "service name")).Append(":\n");
          sb.Append("    models:\n");

          // Computed per service group on purpose: short and long forms never mix within one service.
          var asMap = group.Any(b => b.IsLong);
          foreach (var binding in group)
          {
            if (asMap)
            {
              sb.Append("      ").Append(ValidateKey(binding.ModelKey, "model key")).Append(":\n");
              if (binding.EndpointVar != null)
                sb.Append("        endpoint_var: ").Append(ValidateEnvName(binding.EndpointVar)).Append('\n');
              if (binding.ModelVar != null)
                sb.Append("        model_var: ").Append(ValidateEnvName(binding.ModelVar)).Append('\n');
            }
            else
            {
              sb.Append("      - ").Append(ValidateKey(binding.ModelKey, "model key")).Append('\n');
            }
          }
        }
      }

      if (_models.Count > 0)
      {
        sb.Append("models:\n");
        foreach (var model in _models)
        {
          sb.Append("  ").Append(ValidateKey(model.Key, "model key")).Append(":\n");
          sb.Append("    model: ").Append(ValidateModelReference(model.Model)).Append('\n');
          if (model.ContextSize.HasValue)
            sb.Append("    context_size: ").Append(model.ContextSize.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
          if (model.RuntimeFlags is { Count: > 0 })
          {
            sb.Append("    runtime_flags:\n");
            foreach (var flag in model.RuntimeFlags)
              sb.Append("      - ").Append(QuoteScalar(flag)).Append('\n');
          }
        }
      }

      return sb.ToString();
    }

    /// <summary>Writes the overlay to a file and returns the path.</summary>
    /// <param name="path">The output path.</param>
    /// <returns>The output path.</returns>
    public string WriteOverlay(string path)
    {
      File.WriteAllText(path, EmitOverlay());
      return path;
    }

    private static string ValidateKey(string value, string what)
    {
      if (string.IsNullOrEmpty(value) || !KeyPattern.IsMatch(value))
        throw new ArgumentException(
            $"Invalid Compose {what} '{Describe(value)}': must match [A-Za-z0-9][A-Za-z0-9._-]* (no whitespace, line breaks or YAML metacharacters).");
      return value;
    }

    private static string ValidateEnvName(string value) => ModelEnvName.Validate(value, "envName");

    private static string ValidateModelReference(string value)
    {
      if (string.IsNullOrEmpty(value) || !ModelReferencePattern.IsMatch(value))
        throw new ArgumentException(
            $"Invalid model reference '{Describe(value)}': contains characters that are not allowed in a Compose model reference (no whitespace, line breaks or YAML metacharacters).");
      return value;
    }

    /// <summary>
    /// Emits a free-form scalar (runtime flag) as an escaped double-quoted YAML
    /// scalar. Line breaks and control characters are rejected outright (they have no
    /// legitimate place in a flag and are the primary injection vector); <c>\</c> and
    /// <c>"</c> are escaped so the value can never terminate the quoted scalar early.
    /// </summary>
    private static string QuoteScalar(string value)
    {
      value ??= string.Empty;
      foreach (var ch in value)
      {
        if (ch < 0x20 || ch == 0x7f)
          throw new ArgumentException(
              $"Invalid runtime flag '{Describe(value)}': line breaks and control characters are not allowed.");
      }

      return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    /// <summary>Renders a value for an error message with line breaks made visible.</summary>
    private static string Describe(string value) =>
        value == null ? "<null>" : value.Replace("\r", "\\r").Replace("\n", "\\n");

    private sealed class SpecBuilder(string key) : IComposeModelSpecBuilder
    {
      private readonly string _key = key;
      private string _model;
      private int? _contextSize;
      private string[] _flags;

      public IComposeModelSpecBuilder WithModel(string reference)
      {
        _model = reference;
        return this;
      }

      public IComposeModelSpecBuilder WithContextSize(int tokens)
      {
        if (tokens <= 0)
          throw new ArgumentOutOfRangeException(nameof(tokens), tokens, "Context size must be greater than zero.");

        _contextSize = tokens;
        return this;
      }

      public IComposeModelSpecBuilder WithRuntimeFlags(params string[] flags)
      {
        _flags = flags;
        return this;
      }

      public ComposeModelSpec Build() => new()
      {
        Key = _key,
        Model = _model,
        ContextSize = _contextSize,
        RuntimeFlags = _flags
      };
    }
  }
}
