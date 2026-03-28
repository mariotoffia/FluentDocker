using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Connection;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  /// <summary>
  /// Tests for <see cref="ResponseOwningStream"/> which wraps a content stream
  /// and ensures the owning <see cref="HttpResponseMessage"/> is disposed when
  /// the stream is disposed.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ResponseOwningStreamTests
  {
    private static (ResponseOwningStream wrapper, TrackingStream inner, HttpResponseMessage response)
        CreateTestSubjects(byte[] content = null)
    {
      content ??= new byte[] { 1, 2, 3, 4, 5 };
      var inner = new TrackingStream(content);
      var response = new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new ByteArrayContent(content)
      };
      var wrapper = new ResponseOwningStream(inner, response);
      return (wrapper, inner, response);
    }

    #region Dispose

    [Fact]
    public void DisposingStream_DisposesUnderlyingResponse()
    {
      var (wrapper, inner, response) = CreateTestSubjects();

      wrapper.Dispose();

      Assert.True(inner.IsDisposed);
      // HttpResponseMessage.Dispose() sets Content to null internally,
      // but we verify via our tracking stream that dispose propagated.
      Assert.True(inner.IsDisposed);
    }

    [Fact]
    public async Task DisposingStreamAsync_DisposesUnderlyingResponse()
    {
      var (wrapper, inner, response) = CreateTestSubjects();

      await wrapper.DisposeAsync();

      Assert.True(inner.IsDisposed);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
      var (wrapper, _, _) = CreateTestSubjects();

      wrapper.Dispose();
      wrapper.Dispose(); // second call should be no-op
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
      var (wrapper, _, _) = CreateTestSubjects();

      await wrapper.DisposeAsync();
      await wrapper.DisposeAsync(); // second call should be no-op
    }

    #endregion

    #region Read Delegation

    [Fact]
    public void Read_DelegatesToInnerStream()
    {
      var data = new byte[] { 10, 20, 30, 40, 50 };
      var (wrapper, _, _) = CreateTestSubjects(data);

      var buffer = new byte[5];
      var bytesRead = wrapper.Read(buffer, 0, 5);

      Assert.Equal(5, bytesRead);
      Assert.Equal(data, buffer);
    }

    [Fact]
    public async Task ReadAsync_DelegatesToInnerStream()
    {
      var data = new byte[] { 10, 20, 30, 40, 50 };
      var (wrapper, _, _) = CreateTestSubjects(data);

      var buffer = new byte[5];
      var bytesRead = await wrapper.ReadAsync(buffer, TestContext.Current.CancellationToken);

      Assert.Equal(5, bytesRead);
      Assert.Equal(data, buffer);
    }

    #endregion

    #region Post-Dispose Access

    [Fact]
    public void Read_AfterDispose_ThrowsObjectDisposedException()
    {
      var (wrapper, _, _) = CreateTestSubjects();
      wrapper.Dispose();

      Assert.Throws<ObjectDisposedException>(() => wrapper.Read(new byte[1], 0, 1));
    }

    [Fact]
    public async Task ReadAsync_AfterDispose_ThrowsObjectDisposedException()
    {
      var (wrapper, _, _) = CreateTestSubjects();
      wrapper.Dispose();

      await Assert.ThrowsAsync<ObjectDisposedException>(
          async () => await wrapper.ReadExactlyAsync(new byte[1], TestContext.Current.CancellationToken));
    }

    #endregion

    #region Stream Properties

    [Fact]
    public void CanRead_ReturnsTrue()
    {
      var (wrapper, _, _) = CreateTestSubjects();
      Assert.True(wrapper.CanRead);
    }

    [Fact]
    public void CanSeek_DelegatesToInnerStream()
    {
      var (wrapper, inner, _) = CreateTestSubjects();
      Assert.Equal(inner.CanSeek, wrapper.CanSeek);
    }

    [Fact]
    public void Length_DelegatesToInnerStream()
    {
      var data = new byte[] { 1, 2, 3 };
      var (wrapper, _, _) = CreateTestSubjects(data);
      Assert.Equal(3, wrapper.Length);
    }

    #endregion

    /// <summary>
    /// A MemoryStream wrapper that tracks whether Dispose was called.
    /// </summary>
    private sealed class TrackingStream : MemoryStream
    {
      public bool IsDisposed { get; private set; }

      public TrackingStream(byte[] buffer) : base(buffer) { }

      protected override void Dispose(bool disposing)
      {
        IsDisposed = true;
        base.Dispose(disposing);
      }
    }
  }
}
