using System;
using System.IO;
using System.Reflection;

namespace FluentDocker.Tests.Mocks
{
  /// <summary>
  /// Loads Docker Model Runner test fixtures (captured from a real DMR v1.2.1)
  /// from embedded resources under <c>Fixtures/Dmr/</c>.
  /// </summary>
  internal static class DmrFixtures
  {
    private const string Prefix = "FluentDocker.Tests.Fixtures.Dmr.";
    private static readonly Assembly Asm = typeof(DmrFixtures).Assembly;

    /// <summary>Loads a fixture as text, e.g. <c>Load("chat.sse")</c>.</summary>
    /// <param name="name">The fixture file name (e.g. <c>ls.json</c>).</param>
    /// <returns>The fixture content.</returns>
    public static string Load(string name)
    {
      using var stream = Open(name);
      using var reader = new StreamReader(stream);
      return reader.ReadToEnd();
    }

    /// <summary>Loads a fixture as raw bytes.</summary>
    /// <param name="name">The fixture file name.</param>
    /// <returns>The fixture bytes.</returns>
    public static byte[] LoadBytes(string name)
    {
      using var stream = Open(name);
      using var ms = new MemoryStream();
      stream.CopyTo(ms);
      return ms.ToArray();
    }

    private static Stream Open(string name)
    {
      var resource = Prefix + name;
      return Asm.GetManifestResourceStream(resource)
          ?? throw new InvalidOperationException(
              $"DMR fixture not found: {resource}. Available: {string.Join(", ", Asm.GetManifestResourceNames())}");
    }
  }
}
