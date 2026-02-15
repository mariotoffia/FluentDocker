using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentDocker.Extensions;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Extensions
{
  /// <summary>
  /// Unit tests for HTTP extension methods including Wget.
  /// </summary>
  [Trait("Category", "Unit")]
  public class HttpExtensionTests
  {
    [Fact]
    public async Task Wget_OnNullUrl_ReturnsEmptyString()
    {
      // Arrange
      string? url = null;

      // Act
      var result = await url.Wget();

      // Assert
      Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task Wget_OnEmptyUrl_ReturnsEmptyString()
    {
      // Arrange
      var url = string.Empty;

      // Act
      var result = await url.Wget();

      // Assert
      Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task Wget_OnInvalidUrl_ReturnsEmptyString()
    {
      // Arrange - invalid URL that will fail
      var url = "http://invalid-url-that-definitely-does-not-exist-12345.local/test";

      // Act
      var result = await url.Wget();

      // Assert - should return empty string on failure, not throw
      Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task Wget_OnMalformedUrl_ReturnsEmptyString()
    {
      // Arrange - malformed URL
      var url = "not-a-valid-url";

      // Act
      var result = await url.Wget();

      // Assert
      Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task DoRequest_OnInvalidUrl_ReturnsErrorResponse()
    {
      // Arrange
      var url = "http://invalid-url-12345.local/test";

      // Act
      var result = await HttpExtensions.DoRequest(url);

      // Assert - either has an error or a non-success status code
      Assert.True(result.Err != null || result.Code == 0 || (int)result.Code >= 400);
    }

    [Fact]
    public async Task DoRequest_OnNullUrl_ReturnsErrorResponse()
    {
      // Act
      var result = await HttpExtensions.DoRequest(null);

      // Assert - should have an error
      Assert.True(result.Err != null || result.Code == 0);
    }

    [Fact]
    public void WgetMethod_IsExtensionMethod()
    {
      // Verify Wget is an extension method that can be called on string
      var url = "http://example.com";

      // This compiles only if Wget is an extension method on string
      var task = url.Wget();

      Assert.NotNull(task);
    }

    [Fact]
    public async Task Wget_ReturnsTask_CanBeAwaited()
    {
      // Arrange
      var url = "http://localhost:99999/test"; // Port that doesn't exist

      // Act
      var result = await url.Wget();

      // Assert - should complete without throwing
      Assert.NotNull(result);
      Assert.Equal(string.Empty, result);
    }
  }
}
