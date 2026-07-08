using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using FluentDocker.Kernel;

namespace FluentDocker.Testing.Core
{
  internal static class ProcessExitReaper
  {
    private static readonly ConcurrentDictionary<string, Registration> Registrations = new();
    private static int _registered;
    private static int _cleanupStarted;
    private static PosixSignalRegistration _sigIntRegistration;
    private static PosixSignalRegistration _sigTermRegistration;

    internal static void Register(
        FluentDockerKernel kernel,
        string driverId,
        DockerResourceOptions options)
    {
      if (!IsEnabled() ||
          !options.EnableSessionLabels ||
          string.IsNullOrWhiteSpace(options.SessionId) ||
          IsSharedSession(options.SessionId))
        return;

      Registrations.TryAdd(
          $"{driverId}:{options.SessionId}",
          new Registration(kernel, driverId, options.SessionId, options.TeardownTimeout));
      EnsureHooksRegistered();
    }

    private static bool IsEnabled()
    {
      var value = Environment.GetEnvironmentVariable(SessionLabel.ReaperEnvironmentVariable);
      return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSharedSession(string sessionId) =>
        string.Equals(
            Environment.GetEnvironmentVariable(SessionLabel.SessionEnvironmentVariable),
            sessionId,
            StringComparison.Ordinal);

    private static void EnsureHooksRegistered()
    {
      if (Interlocked.Exchange(ref _registered, 1) != 0)
        return;

      AppDomain.CurrentDomain.ProcessExit += (_, _) => RunCleanup();
      try
      {
        // ponytail: SIGKILL and hard host termination cannot be caught; next-run label reaping is the fallback.
        _sigIntRegistration = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnPosixSignal);
        _sigTermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnPosixSignal);
      }
      catch (PlatformNotSupportedException)
      {
      }
    }

    private static void OnPosixSignal(PosixSignalContext context)
    {
      context.Cancel = true;
      RunCleanup();
      Environment.Exit(context.Signal == PosixSignal.SIGINT ? 130 : 143);
    }

    private static void RunCleanup()
    {
      if (Interlocked.Exchange(ref _cleanupStarted, 1) != 0)
        return;

      foreach (var registration in Registrations.Values)
      {
        try
        {
          using var cts = new CancellationTokenSource(registration.Timeout);
          OrphanCleanup.CleanupSessionResourcesAsync(
              registration.Kernel,
              registration.DriverId,
              registration.SessionId,
              cts.Token).GetAwaiter().GetResult();
        }
        catch
        {
        }
      }
    }

    private sealed record Registration(
        FluentDockerKernel Kernel,
        string DriverId,
        string SessionId,
        TimeSpan Timeout);
  }
}
