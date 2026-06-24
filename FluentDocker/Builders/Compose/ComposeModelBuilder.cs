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
  /// the <c>models:</c> shape), plus a parser to read the same shape back.
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

    /// <summary>The accumulated model specs.</summary>
    public IReadOnlyList<ComposeModelSpec> Models => _models;

    /// <summary>The accumulated service bindings.</summary>
    public IReadOnlyList<ComposeServiceModelBinding> Bindings => _bindings;

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

    /// <summary>
    /// Parses the <c>models:</c> map and per-service <c>models:</c> bindings out of a
    /// Compose document (the inverse of <see cref="EmitOverlay"/>).
    /// </summary>
    /// <remarks>
    /// This is a deliberately narrow, hand-rolled line/indent reader — NOT a general
    /// YAML parser. It recognises ONLY the top-level <c>models:</c> map and the
    /// per-service <c>models:</c> subset that <see cref="EmitOverlay"/> produces, and it
    /// assumes strict 2-space indentation (2/4/6/8 spaces for the respective nesting
    /// levels). It does NOT support YAML flow style (<c>{}</c>/<c>[]</c>), block scalars
    /// (<c>|</c>/<c>&gt;</c>), anchors/aliases, tabs, or comments inside the
    /// <c>models:</c> regions; any of those will be mis-parsed or ignored. It is intended
    /// for re-reading FluentDocker-emitted overlays plus simple hand-written
    /// <c>models:</c> blocks, and must NOT be pointed at arbitrary Compose documents.
    /// </remarks>
    /// <param name="yaml">The compose YAML.</param>
    /// <returns>A populated builder.</returns>
    public static ComposeModelBuilder Parse(string yaml)
    {
      var builder = new ComposeModelBuilder();
      if (string.IsNullOrEmpty(yaml))
        return builder;

      var lines = yaml.Replace("\r\n", "\n").Split('\n');
      ParseTopLevelModels(lines, builder);
      ParseServiceBindings(lines, builder);
      return builder;
    }

    // Counts leading spaces only. The parser keys off exact indent values (2/4/6/8),
    // so it has a hard dependency on 2-space indentation and treats tabs as content.
    private static int Indent(string line)
    {
      var i = 0;
      while (i < line.Length && line[i] == ' ')
        i++;
      return i;
    }

    private static bool IsBlank(string line) => string.IsNullOrWhiteSpace(line);

    private static void ParseTopLevelModels(string[] lines, ComposeModelBuilder builder)
    {
      var inModels = false;
      string key = null;
      string model = null;
      int? context = null;
      List<string> flags = null;
      var inFlags = false;

      void Flush()
      {
        if (key != null && model != null)
        {
          builder._models.Add(new ComposeModelSpec
          {
            Key = key,
            Model = model,
            ContextSize = context,
            RuntimeFlags = flags
          });
        }

        key = null;
        model = null;
        context = null;
        flags = null;
        inFlags = false;
      }

      foreach (var raw in lines)
      {
        if (IsBlank(raw))
          continue;

        var indent = Indent(raw);
        var line = raw.Trim();

        if (indent == 0)
        {
          Flush();
          inModels = line == "models:";
          continue;
        }

        if (!inModels)
          continue;

        if (indent == 2 && line.EndsWith(':'))
        {
          Flush();
          key = line[..^1].Trim();
        }
        else if (indent == 4 && line.StartsWith("model:", StringComparison.Ordinal))
        {
          model = line["model:".Length..].Trim();
          inFlags = false;
        }
        else if (indent == 4 && line.StartsWith("context_size:", StringComparison.Ordinal))
        {
          if (int.TryParse(line["context_size:".Length..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            context = n;
          inFlags = false;
        }
        else if (indent == 4 && line == "runtime_flags:")
        {
          flags = [];
          inFlags = true;
        }
        else if (indent >= 6 && inFlags && line.StartsWith("- ", StringComparison.Ordinal))
        {
          flags!.Add(Unquote(line[2..].Trim()));
        }
      }

      Flush();
    }

    private static void ParseServiceBindings(string[] lines, ComposeModelBuilder builder)
    {
      var inServices = false;
      string service = null;
      var inModels = false;
      string mapKey = null;
      string endpointVar = null;
      string modelVar = null;

      void FlushMapEntry()
      {
        if (service != null && mapKey != null)
        {
          builder._bindings.Add(new ComposeServiceModelBinding
          {
            Service = service,
            ModelKey = mapKey,
            EndpointVar = endpointVar,
            ModelVar = modelVar
          });
        }

        mapKey = null;
        endpointVar = null;
        modelVar = null;
      }

      foreach (var raw in lines)
      {
        if (IsBlank(raw))
          continue;

        var indent = Indent(raw);
        var line = raw.Trim();

        if (indent == 0)
        {
          FlushMapEntry();
          inServices = line == "services:";
          service = null;
          inModels = false;
          continue;
        }

        if (!inServices)
          continue;

        if (indent == 2 && line.EndsWith(':'))
        {
          FlushMapEntry();
          service = line[..^1].Trim();
          inModels = false;
        }
        else if (indent == 4 && line == "models:")
        {
          inModels = true;
        }
        else if (indent == 4 && line.EndsWith(':'))
        {
          inModels = false; // a different service key (e.g. image:) — actually image: has a value
        }
        else if (inModels && indent == 6 && line.StartsWith("- ", StringComparison.Ordinal))
        {
          builder._bindings.Add(new ComposeServiceModelBinding { Service = service, ModelKey = line[2..].Trim() });
        }
        else if (inModels && indent == 6 && line.EndsWith(':'))
        {
          FlushMapEntry();
          mapKey = line[..^1].Trim();
        }
        else if (inModels && indent == 8 && line.StartsWith("endpoint_var:", StringComparison.Ordinal))
        {
          endpointVar = line["endpoint_var:".Length..].Trim();
        }
        else if (inModels && indent == 8 && line.StartsWith("model_var:", StringComparison.Ordinal))
        {
          modelVar = line["model_var:".Length..].Trim();
        }
        else if (indent <= 4)
        {
          // left the models: block (e.g. another service-level key)
          if (line != "models:")
            inModels = false;
        }
      }

      FlushMapEntry();
    }

    private static string Unquote(string value)
    {
      if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
        return value;

      // Reverse QuoteScalar's escaping (\\ -> \, \" -> "). A NUL placeholder guards the
      // backslash pass so an escaped backslash is not re-interpreted; NUL can never
      // occur in a round-tripped value (QuoteScalar rejects control characters).
      return value[1..^1]
          .Replace("\\\\", "\0")
          .Replace("\\\"", "\"")
          .Replace("\0", "\\");
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
