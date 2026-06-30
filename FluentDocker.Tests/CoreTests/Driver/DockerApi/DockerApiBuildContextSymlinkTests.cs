using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
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
    private static Stream InvokeCreateBuildContextTar(string contextPath)
    {
      var method = typeof(DockerApiImageDriver).GetMethod(
          "CreateBuildContextTar", BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      var config = new ImageBuildConfig { BuildContext = contextPath };
      return (Stream)method.Invoke(null, new object[] { contextPath, config })!;
    }

    [Fact]
    public void CreateBuildContextTar_SkipsEscapingSymlinks_KeepsInContextFiles()
    {
      var root = Path.Combine(Path.GetTempPath(), "fd-ctx-" + Guid.NewGuid().ToString("N"));
      var outside = Path.Combine(Path.GetTempPath(), "fd-out-" + Guid.NewGuid().ToString("N"));
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
          // In-context file symlink (must be dereferenced and included).
          File.CreateSymbolicLink(Path.Combine(root, "inside-link.txt"), Path.Combine(root, "safe.txt"));
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

        HashSet<string> keys;
        using (var tar = InvokeCreateBuildContextTar(root))
          keys = ReadTarFileNames(tar).Select(n => n.Replace('\\', '/')).ToHashSet();

        Assert.Contains("Dockerfile", keys);
        Assert.Contains("safe.txt", keys);
        Assert.Contains("sub/nested.txt", keys);
        Assert.Contains("inside-link.txt", keys); // in-context symlink dereferenced
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
    public void CreateBuildContextTar_SiblingPrefixAndChainEscapes_AreExcluded()
    {
      var baseDir = Path.Combine(Path.GetTempPath(), "fd-sib-" + Guid.NewGuid().ToString("N"));
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

        HashSet<string> keys;
        using (var tar = InvokeCreateBuildContextTar(root))
          keys = ReadTarFileNames(tar).Select(n => n.Replace('\\', '/')).ToHashSet();

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
    public void CreateBuildContextTar_BrokenInContextSymlink_IsSkipped_AndOtherFilesPacked()
    {
      var root = Path.Combine(Path.GetTempPath(), "fd-broken-" + Guid.NewGuid().ToString("N"));
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

        HashSet<string> keys;
        using (var tar = InvokeCreateBuildContextTar(root))
          keys = ReadTarFileNames(tar).Select(n => n.Replace('\\', '/')).ToHashSet();

        Assert.Contains("Dockerfile", keys);
        Assert.Contains("real.txt", keys);
        Assert.DoesNotContain("broken.txt", keys);
      }
      finally
      {
        Directory.Delete(root, true);
      }
    }

    /// <summary>
    /// Minimal ustar reader returning the file-entry names; the writer emits short plain
    /// headers so long-name/prefix extensions are not exercised.
    /// </summary>
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
