using System;
using System.Threading.Tasks;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// C-M1: every public op that dereferences a required <see cref="ModelReference"/> (or
  /// <see cref="ModelPackageRequest"/>) must reject a null argument with a clear
  /// <see cref="ArgumentNullException"/> instead of a raw <see cref="NullReferenceException"/>
  /// or a misleading <c>CommandResponse.Fail</c> that echoes the NRE message under an
  /// unrelated error code.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class DockerCliModelManagementDriverTests
  {
    [Fact]
    public async Task PullAsync_NullModel_ThrowsArgumentNullException()
    {
      var driver = new FakeMgmtDriver { Responder = _ => Ok() };

      var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
          driver.PullAsync(Ctx, null!, cancellationToken: TestContext.Current.CancellationToken));

      Assert.Equal("model", ex.ParamName);
      Assert.Empty(driver.Commands);
    }

    [Fact]
    public async Task InspectAsync_NullModel_ThrowsArgumentNullException()
    {
      var driver = new FakeMgmtDriver { Responder = _ => Ok() };

      var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
          driver.InspectAsync(Ctx, null!, TestContext.Current.CancellationToken));

      Assert.Equal("model", ex.ParamName);
      Assert.Empty(driver.Commands);
    }

    [Fact]
    public async Task RemoveAsync_NullModel_ThrowsArgumentNullException()
    {
      var driver = new FakeMgmtDriver { Responder = _ => Ok() };

      var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
          driver.RemoveAsync(Ctx, null!, cancellationToken: TestContext.Current.CancellationToken));

      Assert.Equal("model", ex.ParamName);
      Assert.Empty(driver.Commands);
    }

    [Fact]
    public async Task TagAsync_NullSource_ThrowsArgumentNullException()
    {
      var driver = new FakeMgmtDriver { Responder = _ => Ok() };

      var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
          driver.TagAsync(Ctx, null!, ModelReference.Parse("ai/b"), TestContext.Current.CancellationToken));

      Assert.Equal("source", ex.ParamName);
      Assert.Empty(driver.Commands);
    }

    [Fact]
    public async Task TagAsync_NullTarget_ThrowsArgumentNullException()
    {
      var driver = new FakeMgmtDriver { Responder = _ => Ok() };

      var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
          driver.TagAsync(Ctx, ModelReference.Parse("ai/a"), null!, TestContext.Current.CancellationToken));

      Assert.Equal("target", ex.ParamName);
      Assert.Empty(driver.Commands);
    }

    [Fact]
    public async Task PushAsync_NullModel_ThrowsArgumentNullException()
    {
      var driver = new FakeMgmtDriver { Responder = _ => Ok() };

      var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
          driver.PushAsync(Ctx, null!, TestContext.Current.CancellationToken));

      Assert.Equal("model", ex.ParamName);
      Assert.Empty(driver.Commands);
    }

    [Fact]
    public async Task PackageAsync_NullRequest_ThrowsArgumentNullException()
    {
      var driver = new FakeMgmtDriver { Responder = _ => Ok() };

      var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
          driver.PackageAsync(Ctx, null!, TestContext.Current.CancellationToken));

      Assert.Equal("request", ex.ParamName);
      Assert.Empty(driver.Commands);
    }
  }
}
