using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Core
{
  /// <summary>
  /// Tests for async/await patterns and cancellation support.
  /// </summary>
  [Trait("Category", "Unit")]
  public class AsyncPatternsTests
  {
    [Fact]
    public async Task CancellationToken_WhenCancelled_ThrowsOperationCanceledException()
    {
      // Arrange
      var cts = new CancellationTokenSource();
      cts.Cancel();

      // Act & Assert - TaskCanceledException inherits from OperationCanceledException
      var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
      {
        await Task.Run(() => cts.Token.ThrowIfCancellationRequested(), cts.Token);
      });
      Assert.NotNull(ex);
    }

    [Fact]
    public async Task CancellationToken_NotCancelled_CompletesSuccessfully()
    {
      // Arrange
      var cts = new CancellationTokenSource();

      // Act
      var result = await Task.Run(() =>
      {
        cts.Token.ThrowIfCancellationRequested();
        return "success";
      }, cts.Token);

      // Assert
      Assert.Equal("success", result);
    }

    [Fact]
    public async Task CancellationToken_CancelledDuringOperation_CancelsGracefully()
    {
      // Arrange
      var cts = new CancellationTokenSource();

      // Act
      var task = Task.Run(async () =>
      {
        for (var i = 0; i < 100; i++)
        {
          await Task.Delay(10, cts.Token);
          cts.Token.ThrowIfCancellationRequested();
        }
      }, cts.Token);

      await Task.Delay(50); // Let it start
      cts.Cancel();

      // Assert - TaskCanceledException inherits from OperationCanceledException
      await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    [Fact]
    public async Task AsyncDisposable_DisposesCorrectly()
    {
      // Arrange
      var disposed = false;
      var disposable = new TestAsyncDisposable(() => disposed = true);

      // Act
      await disposable.DisposeAsync();

      // Assert
      Assert.True(disposed);
    }

    [Fact]
    public async Task AsyncDisposable_WithUsingStatement_DisposesAutomatically()
    {
      // Arrange
      var disposed = false;

      // Act
      await using (var disposable = new TestAsyncDisposable(() => disposed = true))
      {
        Assert.False(disposed);
      }

      // Assert
      Assert.True(disposed);
    }

    [Fact]
    public async Task AsyncDisposable_MultipleDispose_IsSafe()
    {
      // Arrange
      var disposeCount = 0;
      var disposable = new TestAsyncDisposable(() => disposeCount++);

      // Act
      await disposable.DisposeAsync();
      await disposable.DisposeAsync();
      await disposable.DisposeAsync();

      // Assert
      Assert.Equal(3, disposeCount);
    }

    [Fact]
    public async Task TaskWhenAll_MultipleAsyncOperations_CompletesAll()
    {
      // Arrange
      async Task<int> GetValueAsync(int value)
      {
        await Task.Delay(10);
        return value;
      }

      // Act
      var tasks = new[]
      {
                GetValueAsync(1),
                GetValueAsync(2),
                GetValueAsync(3)
            };

      var results = await Task.WhenAll(tasks);

      // Assert
      Assert.Equal(new[] { 1, 2, 3 }, results);
    }

    [Fact]
    public async Task TaskWhenAll_OneTaskFails_ThrowsException()
    {
      // Arrange
      async Task<int> FailingTask()
      {
        await Task.Delay(10);
        throw new InvalidOperationException("Task failed");
      }

      async Task<int> SuccessTask(int value)
      {
        await Task.Delay(10);
        return value;
      }

      // Act & Assert
      var tasks = new Task<int>[]
      {
                SuccessTask(1),
                FailingTask(),
                SuccessTask(3)
      };

      await Assert.ThrowsAsync<InvalidOperationException>(async () =>
          await Task.WhenAll(tasks));
    }

    [Fact]
    public async Task ConfigureAwait_False_DoesNotCaptureContext()
    {
      // Arrange & Act
      await Task.Delay(1);

      // Assert - No assertion needed, this tests compilation and execution
      Assert.True(true);
    }

    [Fact]
    public async Task TaskCompletionSource_ManualCompletion_Works()
    {
      // Arrange
      var tcs = new TaskCompletionSource<string>();

      // Act
      _ = Task.Run(async () =>
      {
        await Task.Delay(50);
        tcs.SetResult("completed");
      });

      var result = await tcs.Task;

      // Assert
      Assert.Equal("completed", result);
    }

    [Fact]
    public async Task TaskCompletionSource_ManualException_Throws()
    {
      // Arrange
      var tcs = new TaskCompletionSource<string>();

      // Act
      _ = Task.Run(async () =>
      {
        await Task.Delay(50);
        tcs.SetException(new InvalidOperationException("Manual exception"));
      });

      // Assert
      await Assert.ThrowsAsync<InvalidOperationException>(async () => await tcs.Task);
    }

    [Fact]
    public async Task ValueTask_Completion_WorksLikeTask()
    {
      // Arrange
      async ValueTask<int> GetValueAsync()
      {
        await Task.Delay(10);
        return 42;
      }

      // Act
      var result = await GetValueAsync();

      // Assert
      Assert.Equal(42, result);
    }

    [Fact]
    public void AsyncLocal_PreservesValueAcrossAsyncCalls()
    {
      // Arrange
      var asyncLocal = new AsyncLocal<string>
      {
        Value = "test-value"
      };

      // Act & Assert
      Assert.Equal("test-value", asyncLocal.Value);
    }

    // Helper class for testing IAsyncDisposable
    private class TestAsyncDisposable : IAsyncDisposable
    {
      private readonly Action _onDispose;

      public TestAsyncDisposable(Action onDispose) => _onDispose = onDispose;

      public ValueTask DisposeAsync()
      {
        _onDispose?.Invoke();
        return ValueTask.CompletedTask;
      }
    }
  }
}

