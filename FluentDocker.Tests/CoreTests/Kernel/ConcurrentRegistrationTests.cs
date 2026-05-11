using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Kernel
{
  /// <summary>
  /// Concurrent driver registration stress tests for thread safety.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ConcurrentRegistrationTests
  {
    [Fact]
    public async Task RegisterMultipleDrivers_Concurrently_AllSucceed()
    {
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      try
      {
        var tasks = new List<Task>();
        const int count = 10;

        for (var i = 0; i < count; i++)
        {
          var id = $"driver-{i}";
          var pack = new MockDriverPack();
          var context = new DriverContext(id);
          tasks.Add(kernel.RegisterDriverPackAsync(id, pack, context,
              cancellationToken: TestContext.Current.CancellationToken));
        }

        await Task.WhenAll(tasks);

        // All should be registered
        for (var i = 0; i < count; i++)
        {
          Assert.True(kernel.IsDriverRegistered($"driver-{i}"));
        }
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task RegisterDuplicateId_Concurrently_OnlyOneSucceeds()
    {
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      try
      {
        const string driverId = "shared-id";
        var successCount = 0;
        var failCount = 0;
        var tasks = new List<Task>();

        for (var i = 0; i < 5; i++)
        {
          tasks.Add(Task.Run(async () =>
          {
            try
            {
              var pack = new MockDriverPack();
              var context = new DriverContext(driverId);
              await kernel.RegisterDriverPackAsync(driverId, pack, context,
                  cancellationToken: TestContext.Current.CancellationToken);
              Interlocked.Increment(ref successCount);
            }
            catch (DriverException)
            {
              Interlocked.Increment(ref failCount);
            }
          }, TestContext.Current.CancellationToken));
        }

        await Task.WhenAll(tasks);

        // Exactly one should succeed, rest should fail
        Assert.Equal(1, successCount);
        Assert.Equal(4, failCount);
        Assert.True(kernel.IsDriverRegistered(driverId));
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task RegisterAndQuery_Concurrently_NoErrors()
    {
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      try
      {
        // Register a driver first
        var pack = new MockDriverPack();
        await kernel.RegisterDriverPackAsync("base", pack, new DriverContext("base"),
            cancellationToken: TestContext.Current.CancellationToken);

        // Concurrent queries while registering more
        var tasks = new List<Task>();

        // Registration tasks
        for (var i = 0; i < 5; i++)
        {
          var id = $"concurrent-{i}";
          tasks.Add(kernel.RegisterDriverPackAsync(id, new MockDriverPack(), new DriverContext(id),
              cancellationToken: TestContext.Current.CancellationToken));
        }

        // Query tasks
        for (var i = 0; i < 10; i++)
        {
          tasks.Add(Task.Run(() =>
          {
            Assert.True(kernel.IsDriverRegistered("base"));
            return Task.CompletedTask;
          }, TestContext.Current.CancellationToken));
        }

        await Task.WhenAll(tasks);

        // All registrations + base should exist
        Assert.True(kernel.IsDriverRegistered("base"));
        for (var i = 0; i < 5; i++)
        {
          Assert.True(kernel.IsDriverRegistered($"concurrent-{i}"));
        }
      }
      finally
      {
        kernel.Dispose();
      }
    }
  }
}
