using FluentDocker.Common;

namespace FluentDocker.Drivers.Docker.Cli
{
  internal static class CliByteParser
  {
    public static long ParseHumanReadableBytes(string value) =>
        CliOutputParser.ParseByteValue(value);
  }
}
