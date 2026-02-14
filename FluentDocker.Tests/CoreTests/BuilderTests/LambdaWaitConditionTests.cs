using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
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
            BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static object CreateBuilder()
    {
      return Activator.CreateInstance(
          ContainerBuilderType,
          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
          null,
          [new FluentDockerKernel(), "test"],
          null)!;
    }

    private static Task<bool> InvokeWaitForLambda(
        object builder,
        Func<IContainerService, int, int> condition,
        long timeoutMs,
        CancellationToken cancellationToken = default)
    {
      var mockService = new Mock<IContainerService>();
      return (Task<bool>)WaitForLambdaMethod.Invoke(
          builder,
          [mockService.Object, condition, timeoutMs, cancellationToken])!;
    }

    [Fact]
    public async Task Lambda_ReturnsNegative_ImmediateSuccess()
    {
      var builder = CreateBuilder();

      var result = await InvokeWaitForLambda(
          builder,
          (_, _) => -1,
          5000);

      Assert.True(result);
    }

    [Fact]
    public async Task Lambda_ReturnsZeroThenNegative_SucceedsWithoutSpinning()
    {
      var builder = CreateBuilder();
      var callCount = 0;

      var result = await InvokeWaitForLambda(
          builder,
          (_, iteration) =>
          {
            callCount++;
            return iteration < 3 ? 0 : -1;
          },
          5000);

      Assert.True(result);
      Assert.Equal(4, callCount);
    }

    [Fact]
    public async Task Lambda_ReturnsZeroForever_TimesOut()
    {
      var builder = CreateBuilder();

      var result = await InvokeWaitForLambda(
          builder,
          (_, _) => 0,
          200);

      Assert.False(result);
    }

    [Fact]
    public async Task Lambda_ReturnsPositiveDelay_RespectsTimeout()
    {
      var builder = CreateBuilder();
      var callCount = 0;

      var result = await InvokeWaitForLambda(
          builder,
          (_, _) =>
          {
            callCount++;
            return 100;
          },
          800);

      Assert.False(result);
      Assert.True(callCount >= 2, $"Expected at least 2 calls, got {callCount}");
    }

    [Fact]
    public async Task Lambda_RespectsCancellationToken()
    {
      var builder = CreateBuilder();
      using var cts = new CancellationTokenSource(100);

      var result = await InvokeWaitForLambda(
          builder,
          (_, _) => 50,
          10000,
          cts.Token);

      Assert.False(result);
    }
  }
}
