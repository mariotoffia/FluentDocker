using System;
using System.Threading.Tasks;
using FluentDocker.Drivers.Models;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  [Trait("Category", "Unit")]
  public partial class OpenAiModelInferenceDriverTests
  {
    [Fact]
    public async Task ListEngineModelsAsync_404_MentionsEndpointBasePath()
    {
      var conn = new MockModelApiConnection().SetupGet("/models", 404, "route not found");
      var driver = Create(conn);

      var resp = await driver.ListEngineModelsAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.False(resp.Success);
      Assert.Equal(ErrorCodes.ModelInference.RequestFailed, resp.ErrorCode);
      Assert.Contains("endpoint/base path", resp.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListEngineModelsAsync_404WithModelText_StillMeansEndpointBasePath()
    {
      var conn = new MockModelApiConnection().SetupGet("/models", 404, "models not found");
      var driver = Create(conn);

      var resp = await driver.ListEngineModelsAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.False(resp.Success);
      Assert.Equal(ErrorCodes.ModelInference.RequestFailed, resp.ErrorCode);
      Assert.Contains("endpoint/base path", resp.Error, StringComparison.OrdinalIgnoreCase);
    }
  }
}
