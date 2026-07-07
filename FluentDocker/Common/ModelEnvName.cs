#nullable enable
using System;
using System.Text.RegularExpressions;

namespace FluentDocker.Common
{
  /// <summary>
  /// Shared validation for environment-variable names used by the model-runner
  /// builders (container <c>WithModel</c> and Compose <c>models:</c> bindings). The
  /// name must be a strict shell/Compose identifier; this is also the YAML/env
  /// injection boundary, since a newline or metacharacter in an env-var name could
  /// otherwise inject arbitrary entries into an emitted Compose overlay.
  /// </summary>
  internal static class ModelEnvName
  {
    private static readonly Regex Pattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// Validates <paramref name="value"/> as an environment-variable name and returns
    /// it unchanged when valid; throws <see cref="ArgumentException"/> otherwise.
    /// </summary>
    public static string Validate(string value, string paramName)
    {
      if (string.IsNullOrEmpty(value) || !Pattern.IsMatch(value))
        throw new ArgumentException(
            $"Invalid environment variable name '{value}': must match [A-Za-z_][A-Za-z0-9_]*.", paramName);
      return value;
    }
  }
}
