using System;
using System.Globalization;
using System.Linq;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  internal static class DockerCliTimestampParser
  {
    private static readonly string[] OffsetFormats =
    [
      "yyyy-MM-dd HH:mm:ss zzz",
      "yyyy-MM-dd HH:mm:ss.FFFFFFF zzz"
    ];

    private static readonly string[] LocalFormats =
    [
      "yyyy-MM-dd HH:mm:ss",
      "yyyy-MM-dd HH:mm:ss.FFFFFFF"
    ];

    public static bool TryParse(string value, out DateTime created)
    {
      created = default;
      if (string.IsNullOrWhiteSpace(value))
        return false;

      var text = value.Trim();
      if (DateTimeOffset.TryParse(
              text,
              CultureInfo.InvariantCulture,
              DateTimeStyles.AssumeUniversal,
              out var dto))
      {
        created = dto.UtcDateTime;
        return true;
      }

      return TryParseGoTime(text, out created)
          || DateTime.TryParseExact(
              TrimFraction(text),
              LocalFormats,
              CultureInfo.InvariantCulture,
              DateTimeStyles.None,
              out created);
    }

    private static bool TryParseGoTime(string text, out DateTime created)
    {
      created = default;
      var withoutZoneName = StripTrailingZoneName(text);
      var lastSpace = withoutZoneName.LastIndexOf(' ');
      if (lastSpace <= 0)
        return false;

      var datePart = TrimFraction(withoutZoneName[..lastSpace]);
      var offset = NormalizeOffset(withoutZoneName[(lastSpace + 1)..]);
      if (offset == null)
        return false;

      if (!DateTimeOffset.TryParseExact(
              $"{datePart} {offset}",
              OffsetFormats,
              CultureInfo.InvariantCulture,
              DateTimeStyles.None,
              out var dto))
        return false;

      created = dto.UtcDateTime;
      return true;
    }

    private static string StripTrailingZoneName(string text)
    {
      var lastSpace = text.LastIndexOf(' ');
      if (lastSpace <= 0)
        return text;

      var suffix = text[(lastSpace + 1)..];
      return suffix.All(IsAsciiLetter) ? text[..lastSpace] : text;
    }

    private static string NormalizeOffset(string offset)
    {
      if (offset.Length == 6 && (offset[0] == '+' || offset[0] == '-') && offset[3] == ':')
        return offset;
      if (offset.Length == 5 && (offset[0] == '+' || offset[0] == '-')
          && char.IsDigit(offset[1]) && char.IsDigit(offset[2])
          && char.IsDigit(offset[3]) && char.IsDigit(offset[4]))
        return string.Concat(offset.AsSpan(0, 3), ":", offset.AsSpan(3, 2));
      return null;
    }

    private static string TrimFraction(string text)
    {
      var dot = text.IndexOf('.');
      if (dot < 0)
        return text;
      var end = dot + 1;
      while (end < text.Length && char.IsDigit(text[end]))
        end++;
      var digits = Math.Min(end - dot - 1, 7);
      return text[..(dot + 1 + digits)] + text[end..];
    }

    private static bool IsAsciiLetter(char value) =>
        (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');
  }
}
