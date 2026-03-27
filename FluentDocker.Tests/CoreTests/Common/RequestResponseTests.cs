using System.Net;
using FluentDocker.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  /// <summary>
  /// Unit tests for <see cref="RequestResponse"/>.
  /// The constructor is internal and InternalsVisibleTo is not available
  /// (strong-named assembly), so only default struct behavior is verified.
  /// </summary>
  [Trait("Category", "Unit")]
  public class RequestResponseTests
  {
    [Fact]
    public void DefaultStruct_CodeIsZero()
    {
      // Arrange & Act
      var response = default(RequestResponse);

      // Assert
      Assert.Equal((HttpStatusCode)0, response.Code);
    }

    [Fact]
    public void DefaultStruct_BodyIsNull()
    {
      // Arrange & Act
      var response = default(RequestResponse);

      // Assert
      Assert.Null(response.Body);
    }

    [Fact]
    public void DefaultStruct_ErrIsNull()
    {
      // Arrange & Act
      var response = default(RequestResponse);

      // Assert
      Assert.Null(response.Err);
    }

    [Fact]
    public void DefaultStruct_HeadersIsNull()
    {
      // Arrange & Act
      var response = default(RequestResponse);

      // Assert
      Assert.Null(response.Headers);
    }
  }
}
