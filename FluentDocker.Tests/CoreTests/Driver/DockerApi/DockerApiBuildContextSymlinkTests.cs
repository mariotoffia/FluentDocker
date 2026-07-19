using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  /// <summary>
  /// Security tests for build-context tar packaging: symlinks must not be able to
  /// exfiltrate host files outside the context, and directory symlinks must not be
  /// traversed (no escape, no infinite loop). In-context symlinks are still included.
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerApiBuildContextSymlinkTests
  {
    private static async Task<byte[]> BuildAndCaptureTarAsync(string contextPath)
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/build", "{\"aux\":{\"ID\":\"sha256:test\"}}\n");
      var driver = new DockerApiImageDriver(conn);
      driver.Initialize(new DriverContext("docker-api-build-context-test"));

      var result = await driver.BuildAsync(
          new DriverContext("docker-api-build-context-test"),
          new ImageBuildConfig { BuildContext = contextPath }, null!,
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      return conn.GetRequests().Single(r => r.Method == "POST_STREAM").BodyBytes!;
    }

    [Fact]
    public async Task BuildAsync_SkipsEscapingSymlinks_KeepsInContextFiles()
    {
      var root = CreateOutDirectory("ctx");
      var outside = CreateOutDirectory("out");
      Directory.CreateDirectory(root);
      Directory.CreateDirectory(outside);
      try
      {
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "TOP SECRET");
        File.WriteAllText(Path.Combine(root, "Dockerfile"), "FROM scratch\n");
        File.WriteAllText(Path.Combine(root, "safe.txt"), "safe-content");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(root, "sub", "nested.txt"), "nested");

        try
        {
          // In-context relative file symlink (must be emitted as a symlink tar entry).
          File.CreateSymbolicLink(Path.Combine(root, "inside-link.txt"), "safe.txt");
          // File symlink escaping the context (must be skipped — host-file exfiltration).
          File.CreateSymbolicLink(Path.Combine(root, "escape-link.txt"), Path.Combine(outside, "secret.txt"));
          // Directory symlink escaping the context (must not be traversed).
          Directory.CreateSymbolicLink(Path.Combine(root, "evil-dir"), outside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
          Assert.Skip("Symlink creation is not permitted on this platform.");
          return;
        }

        var tar = await BuildAndCaptureTarAsync(root);
        var keys = ReadTarFileNames(tar)
            .Select(n => n.Replace('\\', '/')).ToHashSet();
        var types = ReadTarTypes(tar);

        Assert.Contains("Dockerfile", keys);
        Assert.Contains("safe.txt", keys);
        Assert.Contains("sub/nested.txt", keys);
        Assert.Equal((byte)'2', types["inside-link.txt"]); // in-context symlink preserved
        Assert.DoesNotContain("escape-link.txt", keys); // escaping file symlink skipped
        Assert.DoesNotContain(keys, k => k.StartsWith("evil-dir", StringComparison.Ordinal)); // dir symlink not traversed
        Assert.DoesNotContain(keys, k => k.Contains("secret", StringComparison.Ordinal)); // no host file leaked
      }
      finally
      {
        Directory.Delete(root, true);
        Directory.Delete(outside, true);
      }
    }

    [Fact]
    public async Task BuildAsync_SiblingPrefixAndChainEscapes_AreExcluded()
    {
      var baseDir = CreateOutDirectory("sib");
      var root = Path.Combine(baseDir, "ctx");
      var sibling = Path.Combine(baseDir, "ctx-evil");
      Directory.CreateDirectory(root);
      Directory.CreateDirectory(sibling);
      try
      {
        File.WriteAllText(Path.Combine(sibling, "sibling-secret.txt"), "SIBLING SECRET");
        File.WriteAllText(Path.Combine(root, "Dockerfile"), "FROM scratch\n");

        try
        {
          // Sibling-prefix escape: root is ".../ctx", target ".../ctx-evil/..." — must fail the
          // trailing-separator StartsWith guard rather than be treated as in-context.
          File.CreateSymbolicLink(Path.Combine(root, "sib-link.txt"), Path.Combine(sibling, "sibling-secret.txt"));
          // Multi-hop chain whose final target escapes the context (full-chain resolution).
          File.CreateSymbolicLink(Path.Combine(root, "mid.txt"), Path.Combine(sibling, "sibling-secret.txt"));
          File.CreateSymbolicLink(Path.Combine(root, "chain.txt"), Path.Combine(root, "mid.txt"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
          Assert.Skip("Symlink creation is not permitted on this platform.");
          return;
        }

        var keys = ReadTarFileNames(await BuildAndCaptureTarAsync(root))
            .Select(n => n.Replace('\\', '/')).ToHashSet();

        Assert.Contains("Dockerfile", keys);
        Assert.DoesNotContain("sib-link.txt", keys); // sibling-prefix escape excluded
        Assert.DoesNotContain("mid.txt", keys); // direct escaping link excluded
        Assert.DoesNotContain("chain.txt", keys); // chain resolving outside excluded
        Assert.DoesNotContain(keys, k => k.Contains("secret", StringComparison.Ordinal));
      }
      finally
      {
        Directory.Delete(baseDir, true);
      }
    }

    [Fact]
    public async Task BuildAsync_BrokenInContextSymlink_IsSkipped_AndOtherFilesPacked()
    {
      var root = CreateOutDirectory("broken");
      Directory.CreateDirectory(root);
      try
      {
        File.WriteAllText(Path.Combine(root, "Dockerfile"), "FROM scratch\n");
        File.WriteAllText(Path.Combine(root, "real.txt"), "real-content");

        try
        {
          // Dangling symlink to a non-existent in-context target: resolves "inside" but OpenRead
          // throws — must be skipped, not abort the whole build.
          File.CreateSymbolicLink(Path.Combine(root, "broken.txt"), Path.Combine(root, "does-not-exist.txt"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
          Assert.Skip("Symlink creation is not permitted on this platform.");
          return;
        }

        var keys = ReadTarFileNames(await BuildAndCaptureTarAsync(root))
            .Select(n => n.Replace('\\', '/')).ToHashSet();

        Assert.Contains("Dockerfile", keys);
        Assert.Contains("real.txt", keys);
        Assert.DoesNotContain("broken.txt", keys);
      }
      finally
      {
        Directory.Delete(root, true);
      }
    }

    [Fact]
    public async Task BuildAsync_FileSymlinkThroughInContextDirSymlink_DoesNotLeak()
    {
      var root = CreateOutDirectory("dirsym");
      var outside = CreateOutDirectory("dirout");
      Directory.CreateDirectory(root);
      Directory.CreateDirectory(outside);
      try
      {
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "TOP SECRET");
        File.WriteAllText(Path.Combine(root, "Dockerfile"), "FROM scratch\n");

        try
        {
          // In-context directory symlink pointing outside the context. The enumerator does
          // not traverse it, but a file symlink can still target a path *through* it. The
          // link's final target resolves lexically inside the context (root/evil-dir/secret.txt),
          // yet the kernel redirects the read through evil-dir to the host file — the link
          // must be skipped, not dereferenced, or it exfiltrates a host file.
          Directory.CreateSymbolicLink(Path.Combine(root, "evil-dir"), outside);
          File.CreateSymbolicLink(
              Path.Combine(root, "leak.txt"), Path.Combine(root, "evil-dir", "secret.txt"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
          Assert.Skip("Symlink creation is not permitted on this platform.");
          return;
        }

        var keys = ReadTarFileNames(await BuildAndCaptureTarAsync(root))
            .Select(n => n.Replace('\\', '/')).ToHashSet();

        Assert.Contains("Dockerfile", keys);
        Assert.DoesNotContain("leak.txt", keys); // file symlink through in-context dir symlink excluded
        Assert.DoesNotContain(keys, k => k.StartsWith("evil-dir", StringComparison.Ordinal)); // dir symlink not traversed
        Assert.DoesNotContain(keys, k => k.Contains("secret", StringComparison.Ordinal)); // no host file leaked
      }
      finally
      {
        Directory.Delete(root, true);
        Directory.Delete(outside, true);
      }
    }

    [Fact]
    public async Task BuildAsync_FileSymlinkPivotsThroughDirSymlinkWithDotDot_DoesNotLeak()
    {
      var root = CreateOutDirectory("pivot");
      var outside = CreateOutDirectory("pivotout");
      Directory.CreateDirectory(root);
      Directory.CreateDirectory(Path.Combine(outside, "subdir"));
      try
      {
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "TOP SECRET");
        File.WriteAllText(Path.Combine(root, "Dockerfile"), "FROM scratch\n");

        try
        {
          // In-context directory symlink to an outside subdirectory, plus a file symlink that
          // pivots back out through it with "..". Lexically "root/evil-dir/../secret.txt"
          // collapses to "root/secret.txt" (looks in-context), but the kernel resolves
          // evil-dir to outside/subdir, ".." to outside, and reads outside/secret.txt. A
          // lexical containment check is fooled by the "symlink/.." cancellation; real-path
          // canonicalisation is not.
          Directory.CreateSymbolicLink(Path.Combine(root, "evil-dir"), Path.Combine(outside, "subdir"));
          File.CreateSymbolicLink(
              Path.Combine(root, "leak.txt"), Path.Combine("evil-dir", "..", "secret.txt"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
          Assert.Skip("Symlink creation is not permitted on this platform.");
          return;
        }

        var keys = ReadTarFileNames(await BuildAndCaptureTarAsync(root))
            .Select(n => n.Replace('\\', '/')).ToHashSet();

        Assert.Contains("Dockerfile", keys);
        Assert.DoesNotContain("leak.txt", keys); // symlink/.. pivot excluded
        Assert.DoesNotContain("secret.txt", keys); // collapsed lexical name must not leak either
        Assert.DoesNotContain(keys, k => k.Contains("secret", StringComparison.Ordinal)); // no host file leaked
      }
      finally
      {
        Directory.Delete(root, true);
        Directory.Delete(outside, true);
      }
    }

    [Fact]
    public async Task BuildAsync_FileTarMode_IsNotWorldWritable()
    {
      var root = CreateOutDirectory("mode");
      File.WriteAllText(Path.Combine(root, "Dockerfile"), "FROM scratch\n");
      var secret = Path.Combine(root, "secret.txt");
      File.WriteAllText(secret, "secret");
      if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(secret, UnixFileMode.UserRead | UnixFileMode.UserWrite);

      var tar = await BuildAndCaptureTarAsync(root);
      var modes = ReadTarModes(tar);

      Assert.NotEqual(511, modes["secret.txt"]);
    }

    [Fact]
    public async Task BuildAsync_DockerignoreDirectoryPattern_SkipsIgnoredEmptyDirectory()
    {
      var root = CreateOutDirectory("ignored-empty-dir");
      try
      {
        File.WriteAllText(Path.Combine(root, "Dockerfile"), "FROM scratch\n");
        File.WriteAllText(Path.Combine(root, ".dockerignore"), "sub/\n");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        Directory.CreateDirectory(Path.Combine(root, "keep"));

        var keys = ReadTarFileNames(await BuildAndCaptureTarAsync(root))
            .Select(n => n.Replace('\\', '/')).ToHashSet();

        Assert.DoesNotContain("sub/", keys);
        Assert.Contains("keep/", keys);
      }
      finally
      {
        Directory.Delete(root, true);
      }
    }

    private static string CreateOutDirectory(string name)
    {
      var path = Path.GetFullPath(Path.Combine(
          ".out", "docker-api-build-context", name, Guid.NewGuid().ToString("N")));
      Directory.CreateDirectory(path);
      return path;
    }

    private static List<string> ReadTarFileNames(byte[] bytes)
    {
      using var stream = new MemoryStream(bytes);
      return ReadTarFileNames(stream);
    }

    private static List<string> ReadTarFileNames(Stream stream)
    {
      var names = new List<string>();
      var header = new byte[512];
      while (ReadExactly(stream, header))
      {
        if (Array.TrueForAll(header, b => b == 0))
          break; // end-of-archive marker
        var name = Encoding.ASCII.GetString(header, 0, 100).TrimEnd('\0');
        if (name.Length == 0)
          break;
        var sizeField = Encoding.ASCII.GetString(header, 124, 12).Trim(' ', '\0');
        var size = sizeField.Length == 0 ? 0 : Convert.ToInt64(sizeField, 8);
        names.Add(name);
        for (var i = (size + 511) / 512; i > 0; i--)
          if (!ReadExactly(stream, header))
            return names;
      }
      return names;
    }

    private static Dictionary<string, int> ReadTarModes(byte[] bytes)
    {
      var modes = new Dictionary<string, int>();
      using var stream = new MemoryStream(bytes);
      var header = new byte[512];
      while (ReadExactly(stream, header))
      {
        if (Array.TrueForAll(header, b => b == 0))
          break;
        var name = Encoding.ASCII.GetString(header, 0, 100).TrimEnd('\0');
        var mode = Encoding.ASCII.GetString(header, 100, 8).Trim(' ', '\0');
        var sizeField = Encoding.ASCII.GetString(header, 124, 12).Trim(' ', '\0');
        var size = sizeField.Length == 0 ? 0 : Convert.ToInt64(sizeField, 8);
        modes[name] = Convert.ToInt32(mode, 8);
        for (var i = (size + 511) / 512; i > 0; i--)
          if (!ReadExactly(stream, header))
            return modes;
      }
      return modes;
    }

    private static Dictionary<string, byte> ReadTarTypes(byte[] bytes)
    {
      var types = new Dictionary<string, byte>();
      using var stream = new MemoryStream(bytes);
      var header = new byte[512];
      while (ReadExactly(stream, header))
      {
        if (Array.TrueForAll(header, b => b == 0))
          break;
        var name = Encoding.ASCII.GetString(header, 0, 100).TrimEnd('\0');
        var sizeField = Encoding.ASCII.GetString(header, 124, 12).Trim(' ', '\0');
        var size = sizeField.Length == 0 ? 0 : Convert.ToInt64(sizeField, 8);
        types[name] = header[156];
        for (var i = (size + 511) / 512; i > 0; i--)
          if (!ReadExactly(stream, header))
            return types;
      }
      return types;
    }

    private static bool ReadExactly(Stream stream, byte[] buffer)
    {
      var read = 0;
      while (read < buffer.Length)
      {
        var n = stream.Read(buffer, read, buffer.Length - read);
        if (n == 0)
          return false;
        read += n;
      }
      return true;
    }
  }
}
