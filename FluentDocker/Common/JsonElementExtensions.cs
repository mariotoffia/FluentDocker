using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace FluentDocker.Common
{
  /// <summary>
  /// Extension methods on <see cref="JsonElement"/> that replace the Newtonsoft
  /// <c>JToken</c> / <c>JObject</c> / <c>JArray</c> navigation API.
  /// </summary>
  public static class JsonElementExtensions
  {
    /// <summary>
    /// Looks up a property by name. Returns <c>null</c> if not found or the element is not an object.
    /// </summary>
    public static JsonElement? Prop(this JsonElement el, string name)
    {
      if (el.ValueKind != JsonValueKind.Object)
        return null;
      return el.TryGetProperty(name, out var prop) ? prop : null;
    }

    /// <summary>
    /// Looks up a property by primary name, falling back to an alternate name.
    /// Useful for Podman/Docker inconsistent casing (e.g. "Id" vs "ID").
    /// </summary>
    public static JsonElement? Prop(this JsonElement el, string name1, string name2)
    {
      if (el.ValueKind != JsonValueKind.Object)
        return null;
      if (el.TryGetProperty(name1, out var prop))
        return prop;
      return el.TryGetProperty(name2, out prop) ? prop : null;
    }

    /// <summary>
    /// Looks up a property by primary name, falling back to two alternate names.
    /// </summary>
    public static JsonElement? Prop(this JsonElement el, string name1, string name2, string name3)
    {
      if (el.ValueKind != JsonValueKind.Object)
        return null;
      if (el.TryGetProperty(name1, out var prop))
        return prop;
      if (el.TryGetProperty(name2, out prop))
        return prop;
      return el.TryGetProperty(name3, out prop) ? prop : null;
    }

    /// <summary>
    /// Gets a string property value, or <c>null</c> if the property is missing or not a string.
    /// </summary>
    public static string GetStringOrDefault(this JsonElement el, string propName)
    {
      var prop = el.Prop(propName);
      return prop?.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
    }

    /// <summary>
    /// Gets a string property value, trying multiple property names in order.
    /// </summary>
    public static string GetStringOrDefault(this JsonElement el, string name1, string name2)
    {
      var prop = el.Prop(name1, name2);
      return prop?.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
    }

    /// <summary>
    /// Gets an Int32 property value, or <paramref name="defaultValue"/> if missing or not a number.
    /// </summary>
    public static int GetInt32OrDefault(this JsonElement el, string propName, int defaultValue = 0)
    {
      var prop = el.Prop(propName);
      if (prop == null)
        return defaultValue;
      var p = prop.Value;
      if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v))
        return v;
      if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out v))
        return v;
      return defaultValue;
    }

    /// <summary>
    /// Gets an Int64 property value, or <paramref name="defaultValue"/> if missing or not a number.
    /// </summary>
    public static long GetInt64OrDefault(this JsonElement el, string propName, long defaultValue = 0)
    {
      var prop = el.Prop(propName);
      if (prop == null)
        return defaultValue;
      var p = prop.Value;
      if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var v))
        return v;
      if (p.ValueKind == JsonValueKind.String && long.TryParse(p.GetString(), out v))
        return v;
      return defaultValue;
    }

    /// <summary>
    /// Gets a UInt64 property value, or <paramref name="defaultValue"/> if missing or not a number.
    /// </summary>
    public static ulong GetUInt64OrDefault(this JsonElement el, string propName, ulong defaultValue = 0)
    {
      var prop = el.Prop(propName);
      if (prop == null)
        return defaultValue;
      var p = prop.Value;
      if (p.ValueKind == JsonValueKind.Number && p.TryGetUInt64(out var v))
        return v;
      if (p.ValueKind == JsonValueKind.String && ulong.TryParse(p.GetString(), out v))
        return v;
      return defaultValue;
    }

    /// <summary>
    /// Gets a double property value, or <paramref name="defaultValue"/> if missing or not a number.
    /// </summary>
    public static double GetDoubleOrDefault(this JsonElement el, string propName, double defaultValue = 0)
    {
      var prop = el.Prop(propName);
      if (prop == null)
        return defaultValue;
      var p = prop.Value;
      if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var v))
        return v;
      if (p.ValueKind == JsonValueKind.String && double.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out v))
        return v;
      return defaultValue;
    }

    /// <summary>
    /// Gets a boolean property value, or <paramref name="defaultValue"/> if missing.
    /// </summary>
    public static bool GetBoolOrDefault(this JsonElement el, string propName, bool defaultValue = false)
    {
      var prop = el.Prop(propName);
      if (prop == null)
        return defaultValue;
      var p = prop.Value;
      return p.ValueKind switch
      {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => bool.TryParse(p.GetString(), out var b) ? b : defaultValue,
        _ => defaultValue
      };
    }

    /// <summary>
    /// Gets a DateTime property value, or <see cref="DateTime.MinValue"/> if missing or unparseable.
    /// Replaces the Newtonsoft <c>JTokenType.Date</c> pattern — STJ keeps dates as strings.
    /// </summary>
    public static DateTime GetDateTimeOrDefault(this JsonElement el, string propName)
    {
      var s = el.GetStringOrDefault(propName);
      if (s == null)
        return DateTime.MinValue;
      return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto)
          ? dto.DateTime
          : DateTime.MinValue;
    }

    /// <summary>
    /// Gets a string array from a property. Returns empty array if missing or not an array.
    /// </summary>
    public static string[] GetStringArray(this JsonElement el, string propName)
    {
      var prop = el.Prop(propName);
      if (prop == null || prop.Value.ValueKind != JsonValueKind.Array)
        return Array.Empty<string>();

      var arr = prop.Value;
      var result = new string[arr.GetArrayLength()];
      var i = 0;
      foreach (var item in arr.EnumerateArray())
        result[i++] = item.GetString();
      return result;
    }

    /// <summary>
    /// Handles the Podman quirk where a value can be either a single string or an array of strings.
    /// </summary>
    public static string[] GetStringOrArray(this JsonElement el, string propName)
    {
      var prop = el.Prop(propName);
      if (prop == null || prop.Value.ValueKind == JsonValueKind.Null)
        return Array.Empty<string>();

      if (prop.Value.ValueKind == JsonValueKind.String)
        return new[] { prop.Value.GetString() };

      if (prop.Value.ValueKind == JsonValueKind.Array)
      {
        var arr = prop.Value;
        var result = new string[arr.GetArrayLength()];
        var i = 0;
        foreach (var item in arr.EnumerateArray())
          result[i++] = item.GetString();
        return result;
      }

      return Array.Empty<string>();
    }

    /// <summary>
    /// Gets a <c>Dictionary&lt;string,string&gt;</c> from a JSON object property.
    /// </summary>
    public static Dictionary<string, string> GetStringDictionary(this JsonElement el, string propName)
    {
      var prop = el.Prop(propName);
      if (prop == null || prop.Value.ValueKind != JsonValueKind.Object)
        return new Dictionary<string, string>();

      var dict = new Dictionary<string, string>();
      foreach (var kv in prop.Value.EnumerateObject())
        dict[kv.Name] = kv.Value.GetString() ?? string.Empty;
      return dict;
    }

    /// <summary>
    /// Safely enumerates an array element. Returns an empty enumerable if the element is not an array.
    /// </summary>
    public static JsonElement.ArrayEnumerator EnumerateArraySafe(this JsonElement el)
    {
      return el.ValueKind == JsonValueKind.Array ? el.EnumerateArray() : default;
    }

    /// <summary>
    /// Safely enumerates an object element. Returns an empty enumerable if the element is not an object.
    /// </summary>
    public static JsonElement.ObjectEnumerator EnumerateObjectSafe(this JsonElement el)
    {
      return el.ValueKind == JsonValueKind.Object ? el.EnumerateObject() : default;
    }

    /// <summary>
    /// Deserializes this element to <typeparamref name="T"/> using <see cref="JsonHelper.CaseInsensitiveOptions"/>.
    /// </summary>
    public static T Deserialize<T>(this JsonElement el)
    {
      return el.Deserialize<T>(JsonHelper.CaseInsensitiveOptions);
    }

    /// <summary>
    /// Returns the string value if the element is a string, otherwise <c>null</c>.
    /// </summary>
    public static string GetStringValue(this JsonElement el)
    {
      return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    /// <summary>
    /// Returns <c>true</c> if the element is <see cref="JsonValueKind.Null"/> or <see cref="JsonValueKind.Undefined"/>.
    /// </summary>
    public static bool IsNullOrUndefined(this JsonElement el)
    {
      return el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
    }

    /// <summary>
    /// Returns <c>true</c> if the nullable element is null, or the element is Null/Undefined.
    /// </summary>
    public static bool IsNullOrMissing(this JsonElement? el)
    {
      return el == null || el.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
    }
  }
}
