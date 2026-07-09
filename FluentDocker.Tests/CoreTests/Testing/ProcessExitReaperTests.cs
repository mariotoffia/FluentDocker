using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Volumes;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;
using Container = FluentDocker.Model.Containers.Container;
using DriverContext = FluentDocker.Model.Drivers.DriverContext;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  [Collection(TestingEnvVarsCollection.Name)]
  public class ProcessExitReaperTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public void IsEnabled_UsesOptInEnvironmentVariable()
    {
      var original = Environment.GetEnvironmentVariable(SessionLabel.ReaperEnvironmentVariable);
      try
      {
        var core = new ProcessExitReaperCore(cleanup: (_, _, _, _) => Task.CompletedTask);
        Environment.SetEnvironmentVariable(SessionLabel.ReaperEnvironmentVariable, null);
        Assert.False(core.IsEnabled());

        Environment.SetEnvironmentVariable(SessionLabel.ReaperEnvironmentVariable, "true");
        Assert.True(core.IsEnabled());
      }
      finally
      {
        Environment.SetEnvironmentVariable(SessionLabel.ReaperEnvironmentVariable, original);
      }
    }

    [Fact]
    public void Register_SkipsSharedSession()
    {
      var core = new ProcessExitReaperCore(
          cleanup: (_, _, _, _) => Task.CompletedTask,
          isEnabled: () => true,
          sharedSessionId: () => "shared-session");

      var registered = core.Register(
          Kernel, DriverId,
          new DockerResourceOptions { SessionId = "shared-session" });

      Assert.False(registered);
      Assert.Equal(0, core.RegistrationCount);
    }

    [Fact]
    public void Register_TracksAndUnregistersOwnedSession()
    {
      var core = new ProcessExitReaperCore(
          cleanup: (_, _, _, _) => Task.CompletedTask,
          isEnabled: () => true,
          sharedSessionId: () => null!);
      var options = new DockerResourceOptions { SessionId = "owned-session" };

      Assert.True(core.Register(Kernel, DriverId, options));
      Assert.Equal(1, core.RegistrationCount);

      core.Unregister(Kernel, DriverId, options.SessionId);

      Assert.Equal(0, core.RegistrationCount);
    }

    [Fact]
    public void RunCleanup_CleansOwnSessionsOnce()
    {
      var cleaned = new List<string>();
      var core = new ProcessExitReaperCore(
          cleanup: (_, _, session, _) =>
          {
            cleaned.Add(session);
            return Task.CompletedTask;
          },
          isEnabled: () => true,
          sharedSessionId: () => "shared-session");

      Assert.True(core.Register(
          Kernel, DriverId,
          new DockerResourceOptions { SessionId = "owned-session" }));
      Assert.False(core.Register(
          Kernel, DriverId,
          new DockerResourceOptions { SessionId = "shared-session" }));

      core.RunCleanup();
      core.RunCleanup();

      Assert.Equal(["owned-session"], cleaned);
    }

    [Fact]
    public void RunCleanup_EnforcesExitCleanupBudgetEvenIfCleanupIgnoresCancellation()
    {
      var started = false;
      var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var core = new ProcessExitReaperCore(
          cleanup: (_, _, _, _) =>
          {
            started = true;
            return never.Task;
          },
          isEnabled: () => true,
          sharedSessionId: () => null!);
      Assert.True(core.Register(
          Kernel, DriverId,
          new DockerResourceOptions
          {
            SessionId = "owned-session",
            TeardownTimeout = TimeSpan.FromMilliseconds(50)
          }));

      var elapsed = Stopwatch.StartNew();
      core.RunCleanup();
      elapsed.Stop();

      Assert.True(started);
      Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void RunCleanup_RemovesOnlyRegisteredSessionResources()
    {
      MockPack.SetupContainerList(
          LabeledContainer("owned", "owned-session"),
          LabeledContainer("other", "other-session"));
      MockPack.SetupContainerRemove();
      MockPack.NetworkDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<NetworkListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<Network>>.Ok([]));
      MockPack.VolumeDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<VolumeListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<Volume>>.Ok([]));
      var core = new ProcessExitReaperCore(
          isEnabled: () => true,
          sharedSessionId: () => null!);

      Assert.True(core.Register(
          Kernel, DriverId,
          new DockerResourceOptions { SessionId = "owned-session" }));
      core.RunCleanup();

      VerifyContainerRemove("owned", Times.Once());
      VerifyContainerRemove("other", Times.Never());
    }

    [Fact]
    public async Task RunCleanup_DoesNotHonorAbandonedMarkerFromOtherSession()
    {
      var createEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var createRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var staleRemoveAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var removeCalls = 0;
      MockPack.NetworkDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<NetworkCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            createEntered.TrySetResult();
            await createRelease.Task.ConfigureAwait(false);
            return CommandResponse<NetworkCreateResult>.Ok(
                new NetworkCreateResult { Id = "shared-name" });
          });
      MockPack.NetworkDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              "shared-name",
              It.IsAny<CancellationToken>()))
          .Callback(() =>
          {
            Interlocked.Increment(ref removeCalls);
            staleRemoveAttempted.TrySetResult();
          })
          .ReturnsAsync(CommandResponse<Unit>.Fail("remove failed"));
      var resource = new NetworkResource(
          Kernel,
          config => config.Name = "shared-name",
          new DockerResourceOptions
          {
            CleanupOrphansOnInit = false,
            SessionId = "other-session",
            TeardownTimeout = TimeSpan.FromMilliseconds(100)
          });

      using var cts = CancellationTokenSource.CreateLinkedTokenSource(
          TestContext.Current.CancellationToken);
      var init = resource.InitializeAsync(cts.Token);
      await createEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      await cts.CancelAsync();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => init);
      await resource.DisposeAsync();
      createRelease.SetResult();
      await staleRemoveAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

      MockPack.SetupContainerList(
          LabeledContainer("owned", "owned-session"),
          LabeledContainer("other", "other-session"));
      MockPack.SetupContainerRemove();
      MockPack.SetupNetworkList(LabeledNetwork("shared-name", "other-session"));
      MockPack.VolumeDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<VolumeListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<Volume>>.Ok([]));
      var core = new ProcessExitReaperCore(
          isEnabled: () => true,
          sharedSessionId: () => null!);

      Assert.True(core.Register(
          Kernel, DriverId,
          new DockerResourceOptions { SessionId = "owned-session" }));
      core.RunCleanup();

      Assert.Equal(1, Volatile.Read(ref removeCalls));
      VerifyContainerRemove("owned", Times.Once());
      VerifyContainerRemove("other", Times.Never());
    }

    private void VerifyContainerRemove(string id, Times times)
    {
      MockPack.ContainerDriver.Verify(
          d => d.RemoveAsync(
              It.IsAny<DriverContext>(), id, true, false,
              It.IsAny<CancellationToken>()),
          times);
    }

    private static Container LabeledContainer(string id, string sessionId)
    {
      return new Container
      {
        Id = id,
        Config = new ContainerConfig
        {
          Labels = new Dictionary<string, string>
          {
            [SessionLabel.Key] = sessionId,
            [SessionLabel.ManagedKey] = "true"
          }
        }
      };
    }

    private static Network LabeledNetwork(string name, string sessionId)
    {
      return new Network
      {
        Id = name,
        Name = name,
        Labels = new Dictionary<string, string>
        {
          [SessionLabel.Key] = sessionId,
          [SessionLabel.ManagedKey] = "true"
        }
      };
    }
  }
}
