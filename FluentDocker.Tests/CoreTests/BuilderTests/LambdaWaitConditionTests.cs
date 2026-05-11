using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Services;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Tests for the lambda wait condition timeout behavior in ContainerBuilder.
  /// Validates that WaitForLambdaAsync correctly handles all return values:
  /// negative (success), zero (retry immediately), positive (delay ms).
  /// </summary>
  [Trait("Category", "Unit")]
  public class LambdaWaitConditionTests
  {
    private static readonly Type ContainerBuilderType =
        typeof(Builders.Builder).Assembly.GetType(
            "FluentDocker.Builders.ContainerBuilder")!;

    private static readonly MethodInfo WaitForLambdaMethod =
        ContainerBuilderType.GetMethod(
            "WaitForLambdaAsync",
            BindingFlags.Static | BindingFlags.NonPublic)!;

    private static Task<bool> InvokeWaitForLambda(
        Func<IContainerService, int, int> condition,
        long timeoutMs,
        CancellationToken cancellationToken = default)
    {
      var mockService = new Mock<IContainerService>();
      return (Task<bool>)WaitForLambdaMethod.Invoke(
          null,
          [mockService.Object, condition, timeoutMs, cancellationToken])!;
    }

    [Fact]
    public async Task Lambda_ReturnsNegative_ImmediateSuccess()
    {
      var result = await InvokeWaitForLambda(
          (_, _) => -1,
          5000, TestContext.Current.CancellationToken);

      Assert.True(result);
    }

    [Fact]
    public async Task Lambda_ReturnsZeroThenNegative_SucceedsWithoutSpinning()
    {
      var callCount = 0;

      var result = await InvokeWaitForLambda(
          (_, iteration) =>
          {
            callCount++;
            return iteration < 3 ? 0 : -1;
          },
          5000, TestContext.Current.CancellationToken);

      Assert.True(result);
      Assert.Equal(4, callCount);
    }

    [Fact]
    public async Task Lambda_ReturnsZeroForever_TimesOut()
    {
      var result = await InvokeWaitForLambda(
          (_, _) => 0,
          200, TestContext.Current.CancellationToken);

      Assert.False(result);
    }

    [Fact]
    public async Task Lambda_ReturnsPositiveDelay_RespectsTimeout()
    {
      var callCount = 0;

      var result = await InvokeWaitForLambda(
          (_, _) =>
          {
            callCount++;
            return 500;
          },
          5000, TestContext.Current.CancellationToken);

      Assert.False(result);
      Assert.True(callCount >= 2, $"Expected at least 2 calls, got {callCount}");
    }

    [Fact]
    public async Task Lambda_RespectsCancellationToken()
    {
      using var cts = new CancellationTokenSource(100);

      var result = await InvokeWaitForLambda(
          (_, _) => 50,
          10000,
          cts.Token);

      Assert.False(result);
    }
  }
}
