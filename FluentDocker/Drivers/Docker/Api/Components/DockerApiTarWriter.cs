using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  internal static class DockerApiTarWriter
  {
    private const int BlockSize = 512;
    private const int FileFallbackMode = 420;      // 0644
    private const int ExecutableFallbackMode = 493; // 0755
    private const int SymlinkFallbackMode = 511;    // 0777

    /// <inheritdoc />
    public static async Task WriteFileAsync(
        Stream tar, string entryName, Stream content, DateTimeOffset modified, int mode,
        CancellationToken cancellationToken)
    {
      var length = content.Length;
      await WriteHeaderWithLongNameAsync(tar, entryName, length, modified, (byte)'0',
          mode, cancellationToken).ConfigureAwait(false);
      await CopyExactlyAsync(content, tar, length, cancellationToken).ConfigureAwait(false);
      await PadAsync(tar, length, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public static async Task WriteDirectoryAsync(
        Stream tar, string entryName, DateTimeOffset modified, int mode,
        CancellationToken cancellationToken)
    {
      var name = entryName[^1] == '/' ? entryName : entryName + "/";
      await WriteHeaderWithLongNameAsync(tar, name, 0, modified, (byte)'5', mode,
          cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public static async Task WriteSymlinkAsync(
        Stream tar, string entryName, string linkTarget, DateTimeOffset modified,
        CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(linkTarget))
        throw new ArgumentException("Tar symlink target is required.", nameof(linkTarget));
      if (Encoding.UTF8.GetByteCount(linkTarget) > 100)
        throw new InvalidOperationException($"Tar symlink target is too long: {linkTarget}");
      await WriteHeaderWithLongNameAsync(tar, entryName, 0, modified, (byte)'2',
          SymlinkFallbackMode, cancellationToken, linkTarget.Replace('\\', '/'))
          .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public static async Task FinishAsync(Stream tar, CancellationToken cancellationToken)
    {
      await tar.WriteAsync(new byte[BlockSize], cancellationToken).ConfigureAwait(false);
      await tar.WriteAsync(new byte[BlockSize], cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public static int FileModeFor(string path)
    {
      if (!OperatingSystem.IsWindows())
      {
        try
        {
          return (int)File.GetUnixFileMode(path) & 511;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
      }

      return IsExecutableName(path) ? ExecutableFallbackMode : FileFallbackMode;
    }

    /// <inheritdoc />
    public static int DirectoryModeFor(string path)
    {
      if (!OperatingSystem.IsWindows())
      {
        try
        {
          return (int)File.GetUnixFileMode(path) & 511;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
      }

      return ExecutableFallbackMode;
    }

    private static bool IsExecutableName(string path)
    {
      var ext = Path.GetExtension(path);
      return ext.Equals(".sh", StringComparison.OrdinalIgnoreCase) ||
          ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
          ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
          ext.Equals(".bat", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task CopyExactlyAsync(
        Stream source, Stream destination, long count, CancellationToken cancellationToken)
    {
      var buffer = new byte[81920];
      while (count > 0)
      {
        var read = await source.ReadAsync(
            buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)), cancellationToken)
            .ConfigureAwait(false);
        if (read == 0)
          throw new EndOfStreamException("File changed while writing Docker build context tar.");
        await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
            .ConfigureAwait(false);
        count -= read;
      }
    }

    private static async Task WriteHeaderAsync(
        Stream tar, string entryName, long size, DateTimeOffset modified, byte type, int mode,
        CancellationToken cancellationToken, string linkTarget = null)
    {
      var header = new byte[BlockSize];
      WriteName(header, entryName.Replace('\\', '/'));
      WriteOctal(header, 100, 8, mode);
      WriteOctal(header, 108, 8, 0);
      WriteOctal(header, 116, 8, 0);
      WriteOctal(header, 124, 12, size);
      WriteOctal(header, 136, 12, Math.Max(0, modified.ToUnixTimeSeconds()));
      for (var i = 148; i < 156; i++)
        header[i] = 32;
      header[156] = type;
      if (!string.IsNullOrEmpty(linkTarget))
        WriteAscii(header, 157, 100, linkTarget);
      WriteAscii(header, 257, 6, "ustar");
      WriteAscii(header, 263, 2, "00");

      var checksum = 0;
      foreach (var b in header)
        checksum += b;
      var text = Convert.ToString(checksum, 8).PadLeft(6, '0');
      WriteAscii(header, 148, 6, text);
      header[154] = 0;
      header[155] = 32;

      await tar.WriteAsync(header, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteHeaderWithLongNameAsync(
        Stream tar, string entryName, long size, DateTimeOffset modified, byte type, int mode,
        CancellationToken cancellationToken, string linkTarget = null)
    {
      var name = entryName.Replace('\\', '/');
      if (!CanWriteName(name))
      {
        var bytes = Encoding.UTF8.GetBytes(name);
        await WriteHeaderAsync(tar, "././@LongLink", bytes.Length, modified, (byte)'L',
            FileFallbackMode, cancellationToken).ConfigureAwait(false);
        await tar.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await PadAsync(tar, bytes.Length, cancellationToken).ConfigureAwait(false);
        name = TruncateUtf8(name, 100);
      }

      await WriteHeaderAsync(tar, name, size, modified, type, mode, cancellationToken, linkTarget)
          .ConfigureAwait(false);
    }

    private static bool CanWriteName(string name)
    {
      if (Encoding.UTF8.GetByteCount(name) <= 100)
        return true;

      var split = name.LastIndexOf('/');
      while (split > 0)
      {
        if (Encoding.UTF8.GetByteCount(name[..split]) <= 155 &&
            Encoding.UTF8.GetByteCount(name[(split + 1)..]) <= 100)
          return true;
        split = name.LastIndexOf('/', split - 1);
      }

      return false;
    }

    private static void WriteName(byte[] header, string name)
    {
      if (Encoding.UTF8.GetByteCount(name) <= 100)
      {
        WriteAscii(header, 0, 100, name);
        return;
      }

      var split = name.LastIndexOf('/');
      while (split > 0)
      {
        var prefix = name[..split];
        var leaf = name[(split + 1)..];
        if (Encoding.UTF8.GetByteCount(prefix) <= 155 && Encoding.UTF8.GetByteCount(leaf) <= 100)
        {
          WriteAscii(header, 0, 100, leaf);
          WriteAscii(header, 345, 155, prefix);
          return;
        }
        split = name.LastIndexOf('/', split - 1);
      }

      throw new InvalidOperationException($"Tar entry name is too long: {name}");
    }

    private static void WriteOctal(byte[] header, int offset, int length, long value)
    {
      var text = Convert.ToString(value, 8).PadLeft(length - 1, '0');
      if (text.Length > length - 1)
      {
        // ponytail: ustar octal fields top out below 8 GiB; add base-256 encoding if larger files matter.
        throw new InvalidOperationException($"Tar header numeric field is too large: {value}");
      }
      WriteAscii(header, offset, length - 1, text);
      header[offset + length - 1] = 0;
    }

    private static string TruncateUtf8(string value, int maxBytes)
    {
      while (Encoding.UTF8.GetByteCount(value) > maxBytes)
        value = value[..^1];
      return value;
    }

    private static void WriteAscii(byte[] header, int offset, int length, string value)
    {
      var bytes = Encoding.UTF8.GetBytes(value);
      if (bytes.Length > length)
        throw new InvalidOperationException($"Tar header field is too long: {value}");
      Array.Copy(bytes, 0, header, offset, bytes.Length);
    }

    private static async Task PadAsync(Stream tar, long size, CancellationToken cancellationToken)
    {
      var remainder = size % BlockSize;
      if (remainder == 0)
        return;
      await tar.WriteAsync(new byte[BlockSize - remainder], cancellationToken)
          .ConfigureAwait(false);
    }
  }
}
