#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FluentDocker.Common;
using FluentDocker.Model.Builders;
using FluentDocker.Model.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Services;

namespace FluentDocker.Extensions
{
  /// <summary>
  /// Legacy model conversion and command-line string helpers.
  /// </summary>
  public static class ModelExtensions
  {
    /// <summary>
    /// Appends a size option when the supplied value parses and is within the maximum size.
    /// </summary>
    /// <param name="sb">The command string builder.</param>
    /// <param name="option">The option prefix to append.</param>
    /// <param name="value">The size value.</param>
    /// <param name="maxSize">The maximum accepted size in bytes.</param>
    /// <returns>The same string builder.</returns>
    public static StringBuilder SizeOptionIfValid(this StringBuilder sb, string option, string? value,
      long maxSize = long.MaxValue)
    {
      if (!string.IsNullOrEmpty(value))
      {
#pragma warning disable CS0618 // legacy extension remains the public grammar for this legacy helper
        var num = value.Convert();
#pragma warning restore CS0618
        if (num == long.MinValue)
          return sb;

        if (num <= maxSize)
          sb.Append(' ').Append(option).Append(Quote(value));
      }

      return sb;
    }

    /// <summary>
    /// Appends an option with a <see cref="short"/> value when present.
    /// </summary>
    /// <param name="sb">The command string builder.</param>
    /// <param name="option">The option prefix to append.</param>
    /// <param name="value">The optional value.</param>
    /// <returns>The same string builder.</returns>
    public static StringBuilder OptionIfExists(this StringBuilder sb, string option, short? value)
    {
      if (value.HasValue)
        sb.Append(CultureInfo.InvariantCulture, $" {option}{value.Value}");

      return sb;
    }

    /// <summary>
    /// Appends an option with a string value when present, quoting the value when needed.
    /// </summary>
    /// <param name="sb">The command string builder.</param>
    /// <param name="option">The option prefix to append.</param>
    /// <param name="value">The optional value.</param>
    /// <returns>The same string builder.</returns>
    public static StringBuilder OptionIfExists(this StringBuilder sb, string option, string? value)
    {
      if (!string.IsNullOrEmpty(value))
        sb.Append(' ').Append(option).Append(Quote(value));

      return sb;
    }

    /// <summary>
    /// Appends a flag option when enabled.
    /// </summary>
    /// <param name="sb">The command string builder.</param>
    /// <param name="option">The flag option to append.</param>
    /// <param name="enabled">Whether to append the flag.</param>
    /// <returns>The same string builder.</returns>
    public static StringBuilder OptionIfExists(this StringBuilder sb, string option, bool enabled)
    {
      if (enabled)
        sb.Append(CultureInfo.InvariantCulture, $" {option}");

      return sb;
    }

    /// <summary>
    /// Appends one option per string value, quoting each value when needed.
    /// </summary>
    /// <param name="sb">The command string builder.</param>
    /// <param name="option">The option prefix to append.</param>
    /// <param name="values">The values to append.</param>
    /// <returns>The same string builder.</returns>
    public static StringBuilder OptionIfExists(this StringBuilder sb, string option, string[]? values)
    {
      if (null == values || 0 == values.Length)
        return sb;

      foreach (var value in values)
        sb.Append(' ').Append(option).Append(Quote(value));

      return sb;
    }

    /// <summary>
    /// Appends one option per key/value pair, quoting each rendered pair when needed.
    /// </summary>
    /// <param name="sb">The command string builder.</param>
    /// <param name="option">The option prefix to append.</param>
    /// <param name="values">The key/value pairs to append.</param>
    /// <returns>The same string builder.</returns>
    public static StringBuilder OptionIfExists(this StringBuilder sb, string option, IDictionary<string, string>? values)
    {
      if (null == values || 0 == values.Count)
        return sb;

      foreach (var value in values)
        sb.Append(' ').Append(option).Append(Quote(value.Key + "=" + value.Value));

      return sb;
    }

    /// <summary>
    ///   Strips the hash algorithm prefixed (if any) from the container hash id.
    /// </summary>
    /// <param name="hashAlgAndContainerHash">The hashalg:containerhash string.</param>
    /// <returns>A "raw" container id hash.</returns>
    public static string? ToPlainId(this string? hashAlgAndContainerHash)
    {
      if (hashAlgAndContainerHash == null)
        return null;

      var split = hashAlgAndContainerHash.Split(':');
      return split.Length == 2 ? split[1] : hashAlgAndContainerHash;
    }

    /// <summary>
    /// Converts a container isolation setting to the Docker CLI value.
    /// </summary>
    /// <param name="isolation">The isolation technology.</param>
    /// <returns>The Docker value, or null for the default/unknown value.</returns>
    public static string? ToDocker(this ContainerIsolationTechnology isolation)
    {
      return isolation switch
      {
        ContainerIsolationTechnology.Default => "default",
        ContainerIsolationTechnology.Hyperv => "hyperv",
        ContainerIsolationTechnology.Process => "process",
        _ => null,
      };
    }

    /// <summary>
    /// Converts a string to a <see cref="TemplateString"/>.
    /// </summary>
    /// <param name="str">The template text.</param>
    /// <returns>A template string.</returns>
    public static TemplateString AsTemplate(this string str)
    {
      return new TemplateString(str);
    }

    /// <summary>
    /// Converts a container state model to the public service running state.
    /// </summary>
    /// <param name="state">The container state.</param>
    /// <returns>The corresponding service running state.</returns>
    public static ServiceRunningState ToServiceState(this ContainerState? state)
    {
      if (null == state)
        return ServiceRunningState.Unknown;

      if (state.Dead)
        return ServiceRunningState.Stopped;

      if (state.Restarting)
        return ServiceRunningState.Starting;

      if (state.Paused)
        return ServiceRunningState.Paused;

      if (state.Running)
        return ServiceRunningState.Running;

      var status = state.Status?.ToLowerInvariant() ?? string.Empty;
      return status switch
      {
        "created" or "exited" => ServiceRunningState.Stopped,
        _ => ServiceRunningState.Unknown,
      };
    }

    /// <summary>
    /// Converts a mount access mode to the Docker CLI value.
    /// </summary>
    /// <param name="access">The mount access mode.</param>
    /// <returns>The Docker value.</returns>
    public static string ToDocker(this MountType access)
    {
      return access switch
      {
        MountType.ReadOnly => "ro",
        MountType.ReadWrite => "rw",
        _ => throw new ArgumentOutOfRangeException(nameof(access), access, $"Unsupported mount type: {access}")
      };
    }

    /// <summary>
    /// Appends values to an array and removes duplicates.
    /// </summary>
    /// <param name="arr">The source array.</param>
    /// <param name="values">The values to append.</param>
    /// <returns>A new array containing distinct values.</returns>
    public static string[] ArrayAddDistinct(this string[] arr, params string[] values)
    {
      return [.. ArrayAdd(arr, values)!.Distinct()];
    }

    /// <summary>
    /// Appends values to an array.
    /// </summary>
    /// <param name="arr">The source array.</param>
    /// <param name="values">The values to append.</param>
    /// <returns>A new array, or the source array when no values were supplied.</returns>
    public static string[]? ArrayAdd(this string[]? arr, params string[] values)
    {
      if (null == values || 0 == values.Length)
        return arr;

      if (null == arr)
      {
        var ret = new string[values.Length];
        values.CopyTo(ret, 0);
        return ret;
      }

      var r = new string[arr.Length + values.Length];
      arr.CopyTo(r, 0);
      values.CopyTo(r, arr.Length);
      return r;
    }

    private static string Quote(string value) => CommandLineQuoting.QuoteArgumentIfNeeded(value);
  }
}
