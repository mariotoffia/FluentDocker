using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public class DockerApiErrorMappingTests
  {
    private static DriverContext Ctx => new("docker-api-error-test");

    private static TestableDriverBase CreateDriver(MockDockerApiConnection? mock = null)
    {
      mock ??= new MockDockerApiConnection();
      var driver = new TestableDriverBase(mock);
      driver.Initialize(new DriverContext("docker-api-error-test", "unix:///var/run/docker.sock"));
      return driver;
    }

    #region MapHttpErrorCode

    [Theory]
    [InlineData(400, "API_400")]
    [InlineData(401, "API_401")]
    [InlineData(404, "API_404")]
    [InlineData(409, "API_409")]
    [InlineData(500, "API_500")]
    [InlineData(503, "API_500")]
    [InlineData(502, "API_500")]
    [InlineData(418, "API_400")] // unknown codes below 500 fall to default
    public void MapHttpErrorCode_MapsStatusCodeToExpectedError(
        int statusCode, string expectedCode)
    {
      var driver = CreateDriver();
      Assert.Equal(expectedCode, TestableDriverBase.TestMapHttpErrorCode(statusCode));
    }

    [Fact]
    public void MapHttpErrorCode_400_ReturnsBadRequest()
    {
      var driver = CreateDriver();
      Assert.Equal(ErrorCodes.Api.BadRequest, TestableDriverBase.TestMapHttpErrorCode(400));
    }

    [Fact]
    public void MapHttpErrorCode_401_ReturnsUnauthorized()
    {
      var driver = CreateDriver();
      Assert.Equal(ErrorCodes.Api.Unauthorized, TestableDriverBase.TestMapHttpErrorCode(401));
    }

    [Fact]
    public void MapHttpErrorCode_404_ReturnsNotFound()
    {
      var driver = CreateDriver();
      Assert.Equal(ErrorCodes.Api.NotFound, TestableDriverBase.TestMapHttpErrorCode(404));
    }

    [Fact]
    public void MapHttpErrorCode_500_ReturnsServerError()
    {
      var driver = CreateDriver();
      Assert.Equal(ErrorCodes.Api.ServerError, TestableDriverBase.TestMapHttpErrorCode(500));
    }

    #endregion

    #region MapNotFoundErrorCode

    [Fact]
    public void MapNotFoundErrorCode_404_UsesDefaultCode()
    {
      var driver = CreateDriver();
      var result = TestableDriverBase.TestMapNotFoundErrorCode(404, ErrorCodes.Container.NotFound);
      Assert.Equal(ErrorCodes.Container.NotFound, result);
    }

    [Fact]
    public void MapNotFoundErrorCode_Non404_FallsBackToMapHttpErrorCode()
    {
      var driver = CreateDriver();
      Assert.Equal(ErrorCodes.Api.Conflict,
          TestableDriverBase.TestMapNotFoundErrorCode(409, ErrorCodes.Container.NotFound));
      Assert.Equal(ErrorCodes.Api.ServerError,
          TestableDriverBase.TestMapNotFoundErrorCode(500, ErrorCodes.Container.NotFound));
      Assert.Equal(ErrorCodes.Api.BadRequest,
          TestableDriverBase.TestMapNotFoundErrorCode(400, ErrorCodes.Container.NotFound));
    }

    #endregion

    #region CreateErrorContext

    [Fact]
    public void CreateErrorContext_SetsOperationAndExitCode()
    {
      var driver = CreateDriver();
      var ctx = driver.TestCreateErrorContext("GET /containers/abc/json", 404);

      Assert.Equal("GET /containers/abc/json", ctx.Operation);
      Assert.Equal(404, ctx.ExitCode);
      Assert.Null(ctx.StdOut);
    }

    [Fact]
    public void CreateErrorContext_SetsDriverIdFromContext()
    {
      var driver = CreateDriver();
      var ctx = driver.TestCreateErrorContext("DELETE /containers/x", 200);

      Assert.Equal("docker-api-error-test", ctx.DriverId);
    }

    [Fact]
    public void CreateErrorContext_SetsResponseBodyAsStdOut()
    {
      var driver = CreateDriver();
      var ctx = driver.TestCreateErrorContext(
          "POST /containers/create", 400, "bad request body");

      Assert.Equal("bad request body", ctx.StdOut);
    }

    [Fact]
    public void CreateErrorContext_IncludesHttpStatusCodeInMetadata()
    {
      var driver = CreateDriver();
      var ctx = driver.TestCreateErrorContext("GET /version", 503);

      Assert.True(ctx.Metadata.ContainsKey("HttpStatusCode"));
      Assert.Equal("503", ctx.Metadata["HttpStatusCode"]);
    }

    #endregion

    #region Full error response parsing (via GET request)

    [Fact]
    public async Task GetRequest_404_ExtractsErrorMessageFromJsonBody()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/containers/missing/json", 404,
          @"{""message"":""container not found""}");

      var driver = CreateDriver(mock);

      var result = await driver.TestGetJsonAsync<object>(
          "/containers/missing/json", CancellationToken.None);

      Assert.False(result.Success);
      Assert.Equal(404, result.StatusCode);
      Assert.Equal("container not found", result.ErrorMessage);
    }

    [Fact]
    public async Task GetRequest_500_ExtractsErrorMessageFromJsonBody()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/info", 500,
          @"{""message"":""internal server error""}");

      var driver = CreateDriver(mock);

      var result = await driver.TestGetJsonAsync<object>(
          "/info", CancellationToken.None);

      Assert.False(result.Success);
      Assert.Equal(500, result.StatusCode);
      Assert.Equal("internal server error", result.ErrorMessage);
    }

    #endregion

    /// <summary>
    /// Test subclass that exposes protected members of DockerApiDriverBase.
    /// </summary>
    private class TestableDriverBase : DockerApiDriverBase
    {
      public TestableDriverBase(IDockerApiConnection conn) : base(conn) { }

      public static string TestMapHttpErrorCode(int statusCode) =>
          MapHttpErrorCode(statusCode);

      public static string TestMapNotFoundErrorCode(int statusCode, string defaultCode) =>
          MapNotFoundErrorCode(statusCode, defaultCode);

      public ErrorContext TestCreateErrorContext(
          string op, int statusCode, string? body = null) =>
          CreateErrorContext(op, statusCode, body);

      public Task<ApiResult<T>> TestGetJsonAsync<T>(
          string path, CancellationToken ct) =>
          GetJsonAsync<T>(path, ct);
    }
  }
}
