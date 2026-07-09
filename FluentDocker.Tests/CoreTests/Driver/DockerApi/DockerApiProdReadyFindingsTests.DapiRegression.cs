using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  public sealed partial class DockerApiProdReadyFindingsTests
  {
    [Fact]
    public async Task GetStreamAsync_IdleTimeout_DoesNotWriteLateBurstIntoCallerBuffer()
    {
      using var listener = new TcpListener(IPAddress.Loopback, 0);
      listener.Start();
      var endpoint = (IPEndPoint)listener.LocalEndpoint;
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(
          TestContext.Current.CancellationToken);
      var burst = Enumerable.Repeat((byte)0xAB, 64).ToArray();
      var server = ServeHeadersThenStallThenBurstAsync(
          listener, burst, TimeSpan.FromMilliseconds(300), cts.Token);
      await using var connection = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = $"tcp://127.0.0.1:{endpoint.Port}",
        ApiVersion = "1.45",
        StreamIdleTimeout = TimeSpan.FromMilliseconds(50),
        RequestTimeout = TimeSpan.FromSeconds(5),
        ConnectionTimeout = TimeSpan.FromSeconds(2)
      });

      await using var stream = await connection.GetStreamAsync(
          "/events", TestContext.Current.CancellationToken);
      var callerBuffer = new byte[64];
      await Assert.ThrowsAsync<TimeoutException>(async () =>
          await stream.ReadAsync(callerBuffer.AsMemory(),
              TestContext.Current.CancellationToken).AsTask());
      await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
      cts.Cancel();
      await server;

      Assert.True(callerBuffer.All(b => b == 0),
          "caller buffer was written by an abandoned read");
    }

    [Fact]
    public async Task CopyFromAsync_HardLinkEntry_MaterializesAsFileCopyNotSymlink()
    {
      var outputRoot = Path.Combine(".out", "docker-api-copyfrom-hardlink",
          Guid.NewGuid().ToString("N"));
      var destination = Path.Combine(outputRoot, "dest") + Path.DirectorySeparatorChar;
      Directory.CreateDirectory(outputRoot);
      var tarBytes = await CreateHardLinkTarAsync();
      var mock = new MockDockerApiConnection();
      mock.SetupStreamBytes("/archive", tarBytes);
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(Ctx);
      try
      {
        var result = await driver.CopyFromAsync(Ctx, "ctr", "/src", destination,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var link = new FileInfo(Path.Combine(destination, "sub", "link.txt"));
        Assert.True(link.Exists);
        Assert.Null(link.LinkTarget);
        Assert.Equal("HELLO", await File.ReadAllTextAsync(
            link.FullName, TestContext.Current.CancellationToken));
      }
      finally
      {
        if (Directory.Exists(outputRoot))
          Directory.Delete(outputRoot, recursive: true);
      }
    }

    [Fact]
    public async Task PostStreamAsync_UploadStalls_ThrowsWithoutWaitingForCallerToken()
    {
      using var listener = new TcpListener(IPAddress.Loopback, 0);
      listener.Start();
      var endpoint = (IPEndPoint)listener.LocalEndpoint;
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(
          TestContext.Current.CancellationToken);
      var server = ServeHeadersThenIgnoreBodyAsync(listener, cts.Token);
      await using var connection = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = $"tcp://127.0.0.1:{endpoint.Port}",
        ApiVersion = "1.45",
        ConnectionTimeout = TimeSpan.FromMilliseconds(200),
        RequestTimeout = TimeSpan.FromSeconds(5)
      });
      using var safety = CancellationTokenSource.CreateLinkedTokenSource(
          TestContext.Current.CancellationToken);
      safety.CancelAfter(TimeSpan.FromSeconds(10));
      // 64MB guarantees socket backpressure even on hosts with large autotuned SO_SNDBUF, so the write
      // genuinely stalls (the server never drains the body) rather than being absorbed into kernel buffers.
      var body = new byte[64 * 1024 * 1024];
      using var content = new ByteArrayContent(body);
      var sw = Stopwatch.StartNew();

      var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
          await connection.PostStreamAsync("/build", content, safety.Token));
      sw.Stop();
      cts.Cancel();
      await server;

      Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3),
          $"upload stall not bounded by the watchdog; elapsed {sw.Elapsed}");
      // The watchdog (not the 10s safety token) must be what cancelled — its message is distinctive.
      Assert.Contains("upload stalled", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CopyFromAsync_SymlinkToAlreadyExtractedFile_StaysSymlinkNotCopy()
    {
      // Regression: a symlink whose literal target is an already-extracted regular file must be
      // preserved as a symlink, never silently materialized as a byte-for-byte copy (a docker cp
      // fidelity loss, and the exact case the removed resolve-by-existence heuristic misclassified).
      var outputRoot = Path.Combine(".out", "docker-api-copyfrom-symlink-fidelity",
          Guid.NewGuid().ToString("N"));
      var destination = Path.Combine(outputRoot, "dest") + Path.DirectorySeparatorChar;
      Directory.CreateDirectory(outputRoot);
      var tarBytes = await CreateSymlinkToExtractedFileTarAsync();
      var logs = new List<string>();
      var context = new DriverContext("docker-api-prod-ready-test")
      {
        LoggerFactory = new CollectingLoggerFactory(logs)
      };
      var mock = new MockDockerApiConnection();
      mock.SetupStreamBytes("/archive", tarBytes);
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(context);
      try
      {
        var result = await driver.CopyFromAsync(context, "ctr", "/src", destination,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        // Discriminator: the entry went down the symlink-preservation branch, not the hardlink copy
        // branch. The old resolve-by-existence heuristic copied it and never logged this.
        Assert.Contains(logs, m => m.Contains("link.txt", StringComparison.Ordinal) &&
            m.Contains("preserving symlink", StringComparison.Ordinal));
        var link = new FileInfo(Path.Combine(destination, "link.txt"));
        if (link.LinkTarget != null)
          Assert.Equal("data.txt", link.LinkTarget);
      }
      finally
      {
        if (Directory.Exists(outputRoot))
          Directory.Delete(outputRoot, recursive: true);
      }
    }

    [Fact]
    public async Task CopyFromAsync_HardLinkThroughSymlinkedDir_DoesNotReadOutOfTree()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("Escape scenario relies on POSIX symlink resolution semantics.");

      var outputRoot = Path.Combine(".out", "docker-api-copyfrom-escape",
          Guid.NewGuid().ToString("N"));
      var destination = Path.Combine(outputRoot, "dest") + Path.DirectorySeparatorChar;
      Directory.CreateDirectory(outputRoot);
      // A sensitive file OUTSIDE the destination tree (sibling of the extraction staging dir). The
      // crafted archive's hardlink resolves here through an in-tree symlink; extraction must refuse it.
      const string secret = "TOPSECRET-OUT-OF-TREE";
      await File.WriteAllTextAsync(Path.Combine(outputRoot, "secret.txt"), secret,
          TestContext.Current.CancellationToken);
      var tarBytes = await CreateHardLinkThroughSymlinkTarAsync();
      var mock = new MockDockerApiConnection();
      mock.SetupStreamBytes("/archive", tarBytes);
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(Ctx);
      try
      {
        var result = await driver.CopyFromAsync(Ctx, "ctr", "/src", destination,
            TestContext.Current.CancellationToken);

        // Fail closed: the crafted symlinked-dir traversal is refused before the hardlink resolves, and
        // the out-of-tree secret is never read into the destination tree.
        Assert.False(result.Success);
        var loot = Path.Combine(destination, "loot");
        Assert.False(
            File.Exists(loot) &&
            (await File.ReadAllTextAsync(loot, TestContext.Current.CancellationToken)).Contains(secret),
            "out-of-tree secret was copied into the destination tree");
      }
      finally
      {
        if (Directory.Exists(outputRoot))
          Directory.Delete(outputRoot, recursive: true);
      }
    }

    [Fact]
    public async Task CopyFromAsync_RegularFileThroughSymlinkedDir_DoesNotWriteOutOfTree()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("Escape scenario relies on POSIX symlink resolution semantics.");

      var outputRoot = Path.Combine(".out", "docker-api-copyfrom-write-escape",
          Guid.NewGuid().ToString("N"));
      var destination = Path.Combine(outputRoot, "dest") + Path.DirectorySeparatorChar;
      Directory.CreateDirectory(outputRoot);
      var tarBytes = await CreateRegularFileThroughSymlinkTarAsync();
      var mock = new MockDockerApiConnection();
      mock.SetupStreamBytes("/archive", tarBytes);
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(Ctx);
      try
      {
        var result = await driver.CopyFromAsync(Ctx, "ctr", "/src", destination,
            TestContext.Current.CancellationToken);

        // Fail closed: a regular-file entry whose parent resolves out of tree through a symlink must not
        // be written. 'x'->'.' + 'x/climb'->'..' would land <root>/climb/evil at <outputRoot>/evil.
        Assert.False(result.Success);
        Assert.False(File.Exists(Path.Combine(outputRoot, "evil")),
            "regular-file entry was written outside the destination tree");
      }
      finally
      {
        if (Directory.Exists(outputRoot))
          Directory.Delete(outputRoot, recursive: true);
      }
    }

    [Fact]
    public async Task CopyFromAsync_HardLinkThroughRootLevelSymlink_DoesNotReadOutOfTree()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("Escape scenario relies on POSIX symlink resolution semantics.");

      var outputRoot = Path.Combine(".out", "docker-api-copyfrom-rootgadget",
          Guid.NewGuid().ToString("N"));
      var destination = Path.Combine(outputRoot, "dest") + Path.DirectorySeparatorChar;
      Directory.CreateDirectory(outputRoot);
      const string secret = "TOPSECRET-ROOTGADGET";
      await File.WriteAllTextAsync(Path.Combine(outputRoot, "secret.txt"), secret,
          TestContext.Current.CancellationToken);
      var tarBytes = await CreateHardLinkThroughRootLevelSymlinkTarAsync();
      var mock = new MockDockerApiConnection();
      mock.SetupStreamBytes("/archive", tarBytes);
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(Ctx);
      try
      {
        var result = await driver.CopyFromAsync(Ctx, "ctr", "/src", destination,
            TestContext.Current.CancellationToken);

        // The root-level gadget ('s'->'.', 'link'->'s/..') has a CLEAN parent chain, so the write-side
        // guard accepts both symlinks and 'link' physically resolves to the parent OF the staging root.
        // The escape is stopped on the READ side: the hardlink source walk refuses to copy through the
        // symlinked 'link' component, so extraction succeeds with the hardlink skipped and no leak.
        Assert.True(result.Success);
        var loot = Path.Combine(destination, "loot");
        Assert.False(
            File.Exists(loot) &&
            (await File.ReadAllTextAsync(loot, TestContext.Current.CancellationToken)).Contains(secret),
            "out-of-tree secret was copied into the destination tree");
      }
      finally
      {
        if (Directory.Exists(outputRoot))
          Directory.Delete(outputRoot, recursive: true);
      }
    }

    [Fact]
    public async Task CopyFromAsync_LeafSymlinkThenRegularFile_DoesNotWriteOutOfTree()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("Escape scenario relies on POSIX symlink resolution semantics.");

      var outputRoot = Path.Combine(".out", "docker-api-copyfrom-leaf-regular",
          Guid.NewGuid().ToString("N"));
      var destination = Path.Combine(outputRoot, "dest") + Path.DirectorySeparatorChar;
      Directory.CreateDirectory(outputRoot);
      // Pre-existing sensitive file OUTSIDE the destination tree (sibling of the staging root). The
      // crafted archive plants a symlink AT the leaf entry name that resolves here; a following
      // FileStream(Create) would overwrite it. Extraction must unlink the leaf symlink first.
      const string original = "ORIGINAL-DO-NOT-OVERWRITE";
      var victim = Path.Combine(outputRoot, "victim.txt");
      await File.WriteAllTextAsync(victim, original, TestContext.Current.CancellationToken);
      var tarBytes = await CreateLeafSymlinkThenRegularFileTarAsync();
      var mock = new MockDockerApiConnection();
      mock.SetupStreamBytes("/archive", tarBytes);
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(Ctx);
      try
      {
        var result = await driver.CopyFromAsync(Ctx, "ctr", "/src", destination,
            TestContext.Current.CancellationToken);

        // The leaf symlink is unlinked before the regular file is written, so the write lands in-tree
        // and the out-of-tree victim keeps its original contents.
        Assert.True(result.Success);
        Assert.Equal(original,
            await File.ReadAllTextAsync(victim, TestContext.Current.CancellationToken));
      }
      finally
      {
        if (Directory.Exists(outputRoot))
          Directory.Delete(outputRoot, recursive: true);
      }
    }

    [Fact]
    public async Task CopyFromAsync_LeafSymlinkThenHardLink_DoesNotWriteOutOfTree()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("Escape scenario relies on POSIX symlink resolution semantics.");

      var outputRoot = Path.Combine(".out", "docker-api-copyfrom-leaf-hardlink",
          Guid.NewGuid().ToString("N"));
      var destination = Path.Combine(outputRoot, "dest") + Path.DirectorySeparatorChar;
      Directory.CreateDirectory(outputRoot);
      const string original = "ORIGINAL-DO-NOT-OVERWRITE";
      var victim = Path.Combine(outputRoot, "victim.txt");
      await File.WriteAllTextAsync(victim, original, TestContext.Current.CancellationToken);
      var tarBytes = await CreateLeafSymlinkThenHardLinkTarAsync();
      var mock = new MockDockerApiConnection();
      mock.SetupStreamBytes("/archive", tarBytes);
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(Ctx);
      try
      {
        var result = await driver.CopyFromAsync(Ctx, "ctr", "/src", destination,
            TestContext.Current.CancellationToken);

        // Same primitive, but the write is a hardlink File.Copy(overwrite:true) which also follows a
        // leaf symlink. The unlink-before-write guard keeps it in-tree; the victim is untouched.
        Assert.True(result.Success);
        Assert.Equal(original,
            await File.ReadAllTextAsync(victim, TestContext.Current.CancellationToken));
      }
      finally
      {
        if (Directory.Exists(outputRoot))
          Directory.Delete(outputRoot, recursive: true);
      }
    }

    private static async Task<byte[]> CreateSymlinkToExtractedFileTarAsync()
    {
      await using var ms = new MemoryStream();
      await using (var writer = new TarWriter(ms, TarEntryFormat.Pax, leaveOpen: true))
      {
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, "data.txt")
        {
          DataStream = new MemoryStream(Encoding.UTF8.GetBytes("HELLO"))
        }, TestContext.Current.CancellationToken);
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, "link.txt")
        {
          LinkName = "data.txt"
        }, TestContext.Current.CancellationToken);
      }
      return ms.ToArray();
    }

    private static async Task<byte[]> CreateHardLinkThroughSymlinkTarAsync()
    {
      await using var ms = new MemoryStream();
      await using (var writer = new TarWriter(ms, TarEntryFormat.Pax, leaveOpen: true))
      {
        // 'x' -> '.' (in-tree, accepted), then 'x/climb' -> '..'. Because x resolves to the staging
        // root, climb is physically created at <root>/climb pointing outside the tree. The hardlink
        // below is lexically contained but physically escapes through that symlinked component.
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, "x")
        {
          LinkName = "."
        }, TestContext.Current.CancellationToken);
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, "x/climb")
        {
          LinkName = ".."
        }, TestContext.Current.CancellationToken);
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.HardLink, "loot")
        {
          LinkName = "climb/secret.txt"
        }, TestContext.Current.CancellationToken);
      }
      return ms.ToArray();
    }

    private static async Task<byte[]> CreateRegularFileThroughSymlinkTarAsync()
    {
      await using var ms = new MemoryStream();
      await using (var writer = new TarWriter(ms, TarEntryFormat.Pax, leaveOpen: true))
      {
        // Same primitive as the read escape, but the payload is a regular file written through the
        // symlinked directory component instead of a hardlink copy.
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, "x")
        {
          LinkName = "."
        }, TestContext.Current.CancellationToken);
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, "x/climb")
        {
          LinkName = ".."
        }, TestContext.Current.CancellationToken);
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, "climb/evil")
        {
          DataStream = new MemoryStream(Encoding.UTF8.GetBytes("EVIL-OUT-OF-TREE"))
        }, TestContext.Current.CancellationToken);
      }
      return ms.ToArray();
    }

    private static async Task<byte[]> CreateHardLinkThroughRootLevelSymlinkTarAsync()
    {
      await using var ms = new MemoryStream();
      await using (var writer = new TarWriter(ms, TarEntryFormat.Pax, leaveOpen: true))
      {
        // Root-level gadget: 's' -> '.' (resolves to the staging root), then 'link' -> 's/..'. Both have
        // a clean (symlink-free) parent chain, so the write-side guard accepts them, yet 'link' physically
        // resolves to the parent OF the staging root. The hardlink source then traverses 'link' and must
        // be refused by the source-side reparse-point walk (the write-side guard alone does not catch it).
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, "s")
        {
          LinkName = "."
        }, TestContext.Current.CancellationToken);
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, "link")
        {
          LinkName = "s/.."
        }, TestContext.Current.CancellationToken);
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.HardLink, "loot")
        {
          LinkName = "link/secret.txt"
        }, TestContext.Current.CancellationToken);
      }
      return ms.ToArray();
    }

    private static async Task<byte[]> CreateLeafSymlinkThenRegularFileTarAsync()
    {
      await using var ms = new MemoryStream();
      await using (var writer = new TarWriter(ms, TarEntryFormat.Pax, leaveOpen: true))
      {
        // Duplicate leaf name 'loot': first a laundered out-of-tree symlink ('s'->'.', then
        // 'loot'->'s/../victim.txt' whose '..' collapses lexically past 's' so it looks in-tree), then a
        // regular file of the SAME name. A naive extractor writes the file THROUGH the leaf symlink.
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, "s")
        {
          LinkName = "."
        }, TestContext.Current.CancellationToken);
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, "loot")
        {
          LinkName = "s/../victim.txt"
        }, TestContext.Current.CancellationToken);
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, "loot")
        {
          DataStream = new MemoryStream(Encoding.UTF8.GetBytes("EVIL-CONTENT"))
        }, TestContext.Current.CancellationToken);
      }
      return ms.ToArray();
    }

    private static async Task<byte[]> CreateLeafSymlinkThenHardLinkTarAsync()
    {
      await using var ms = new MemoryStream();
      await using (var writer = new TarWriter(ms, TarEntryFormat.Pax, leaveOpen: true))
      {
        // Same leaf-symlink primitive, but the duplicate 'loot' is a hardlink to an in-tree file; the
        // materializing File.Copy(overwrite:true) also follows the planted leaf symlink out of tree.
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, "data.txt")
        {
          DataStream = new MemoryStream(Encoding.UTF8.GetBytes("EVIL-CONTENT"))
        }, TestContext.Current.CancellationToken);
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, "s")
        {
          LinkName = "."
        }, TestContext.Current.CancellationToken);
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, "loot")
        {
          LinkName = "s/../victim.txt"
        }, TestContext.Current.CancellationToken);
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.HardLink, "loot")
        {
          LinkName = "data.txt"
        }, TestContext.Current.CancellationToken);
      }
      return ms.ToArray();
    }

    private static async Task<byte[]> CreateHardLinkTarAsync()
    {
      await using var ms = new MemoryStream();
      await using (var writer = new TarWriter(ms, TarEntryFormat.Pax, leaveOpen: true))
      {
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, "data.txt")
        {
          DataStream = new MemoryStream(Encoding.UTF8.GetBytes("HELLO"))
        }, TestContext.Current.CancellationToken);
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.HardLink, "sub/link.txt")
        {
          LinkName = "data.txt"
        }, TestContext.Current.CancellationToken);
      }
      return ms.ToArray();
    }

    private static async Task ServeHeadersThenStallThenBurstAsync(
        TcpListener listener, byte[] burstBytes, TimeSpan stallBeforeBurst, CancellationToken ct)
    {
      try
      {
        using var client = await listener.AcceptTcpClientAsync(ct);
        await using var stream = client.GetStream();
        await ReadHeadersAsync(stream, ct);
        var response = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n\r\n");
        await stream.WriteAsync(response, ct);
        await Task.Delay(stallBeforeBurst, ct);
        await stream.WriteAsync(burstBytes, ct);
      }
      catch (OperationCanceledException)
      {
      }
      catch (SocketException)
      {
      }
      catch (ObjectDisposedException)
      {
      }
    }

    private static async Task ServeHeadersThenIgnoreBodyAsync(
        TcpListener listener, CancellationToken ct)
    {
      try
      {
        using var client = await listener.AcceptTcpClientAsync(ct);
        await using var stream = client.GetStream();
        await ReadHeadersAsync(stream, ct);
        await Task.Delay(TimeSpan.FromSeconds(30), ct);
      }
      catch (OperationCanceledException)
      {
      }
      catch (SocketException)
      {
      }
      catch (ObjectDisposedException)
      {
      }
    }
  }
}
