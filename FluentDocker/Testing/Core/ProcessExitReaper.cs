using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// Testable registration/cleanup core used by the process-exit reaper.
  /// </summary>
  public sealed class ProcessExitReaperCore
  {
    private static readonly TimeSpan MaxExitCleanupTimeout = TimeSpan.FromSeconds(5);
    private readonly ConcurrentDictionary<string, Registration> _registrations = new();
    private readonly object _registrationsLock = new();
    private readonly Func<bool> _isEnabled;
    private readonly Func<string?> _sharedSessionId;
    private readonly Func<FluentDockerKernel, string, string, CancellationToken, Task> _cleanup;
    private int _cleanupStarted;

    /// <summary>Creates a reaper core with injectable seams for tests.</summary>
    public ProcessExitReaperCore(
        Func<FluentDockerKernel, string, string, CancellationToken, Task>? cleanup = null,
        Func<bool>? isEnabled = null,
        Func<string?>? sharedSessionId = null)
    {
      _cleanup = cleanup ?? DefaultCleanupAsync;
      _isEnabled = isEnabled ?? IsEnvironmentEnabled;
      _sharedSessionId = sharedSessionId ?? SessionLabel.SharedSessionId;
    }

    /// <summary>Current registration count, exposed for adapter/meta tests.</summary>
    public int RegistrationCount => _registrations.Count;

    /// <summary>Whether process-exit cleanup is enabled.</summary>
    public bool IsEnabled() => _isEnabled();

    /// <summary>Registers a kernel/session for best-effort exit cleanup.</summary>
    public bool Register(FluentDockerKernel kernel, string driverId, DockerResourceOptions options)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(options);
      if (!IsEnabled() ||
          !options.EnableSessionLabels ||
          string.IsNullOrWhiteSpace(options.SessionId) ||
          string.Equals(_sharedSessionId(), options.SessionId, StringComparison.Ordinal))
        return false;

      lock (_registrationsLock)
      {
        _registrations.AddOrUpdate(
            Key(kernel, driverId, options.SessionId),
            _ => new Registration(kernel, driverId, options.SessionId, options.TeardownTimeout),
            (_, existing) =>
            {
              existing.Increment();
              return existing;
            });
      }

      return true;
    }

    /// <summary>Unregisters one resource using the supplied kernel/session.</summary>
    public void Unregister(FluentDockerKernel kernel, string driverId, string sessionId)
    {
      if (kernel == null || string.IsNullOrWhiteSpace(driverId) || string.IsNullOrWhiteSpace(sessionId))
        return;

      lock (_registrationsLock)
      {
        var key = Key(kernel, driverId, sessionId);
        if (_registrations.TryGetValue(key, out var registration) && registration.Decrement() <= 0)
          _registrations.TryRemove(key, out _);
      }
    }

    /// <summary>Runs cleanup once. Subsequent calls are no-ops.</summary>
    public void RunCleanup()
    {
      if (Interlocked.Exchange(ref _cleanupStarted, 1) != 0)
        return;

      KeyValuePair<string, Registration>[] registrations;
      lock (_registrationsLock)
      {
        registrations = _registrations.ToArray();
      }

      foreach (var pair in registrations)
      {
        if (!pair.Value.TryGetKernel(out var kernel))
        {
          lock (_registrationsLock)
          {
            _registrations.TryRemove(pair.Key, out _);
          }
          continue;
        }

        try
        {
          using var cts = new CancellationTokenSource(CappedTimeout(pair.Value.Timeout));
          _cleanup(kernel, pair.Value.DriverId, pair.Value.SessionId, cts.Token)
              .WaitAsync(cts.Token)
              .GetAwaiter().GetResult();
        }
        catch
        {
        }
        finally
        {
          lock (_registrationsLock)
          {
            _registrations.TryRemove(pair.Key, out _);
          }
        }
      }
    }

    private static TimeSpan CappedTimeout(TimeSpan timeout) =>
        timeout < MaxExitCleanupTimeout ? timeout : MaxExitCleanupTimeout;

    private static string Key(FluentDockerKernel kernel, string driverId, string sessionId) =>
        $"{RuntimeHelpers.GetHashCode(kernel)}:{driverId}:{sessionId}";

    private static bool IsEnvironmentEnabled()
    {
      var value = Environment.GetEnvironmentVariable(SessionLabel.ReaperEnvironmentVariable);
      // Default ON so a Ctrl-C mid-run reclaims THIS session's own containers instead of leaking
      // them running (TST-MAJ-1). Only session-labelled resources are reaped, never foreign ones.
      // Opt out explicitly with the env var set to 0/false; 1/true remains an affirmation.
      if (string.IsNullOrEmpty(value))
        return true;
      return !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task DefaultCleanupAsync(
        FluentDockerKernel kernel,
        string driverId,
        string sessionId,
        CancellationToken cancellationToken)
    {
      await OrphanCleanup.CleanupSessionResourcesAsync(
          kernel, driverId, sessionId, cancellationToken).ConfigureAwait(false);
    }

    private sealed class Registration
    {
      private readonly WeakReference<FluentDockerKernel> _kernel;
      private int _count = 1;

      public Registration(
          FluentDockerKernel kernel,
          string driverId,
          string sessionId,
          TimeSpan timeout)
      {
        _kernel = new WeakReference<FluentDockerKernel>(kernel);
        DriverId = driverId;
        SessionId = sessionId;
        Timeout = timeout;
      }

      public string DriverId { get; }

      public string SessionId { get; }

      public TimeSpan Timeout { get; }

      public void Increment() => Interlocked.Increment(ref _count);

      public int Decrement() => Interlocked.Decrement(ref _count);

      public bool TryGetKernel([MaybeNullWhen(false)] out FluentDockerKernel kernel)
      {
        return _kernel.TryGetTarget(out kernel);
      }
    }
  }

  internal static class ProcessExitReaper
  {
    private static readonly ProcessExitReaperCore Core = new();
    private static int _registered;
    private static PosixSignalRegistration? _sigIntRegistration;
    private static PosixSignalRegistration? _sigTermRegistration;

    internal static bool Register(
        FluentDockerKernel kernel,
        string driverId,
        DockerResourceOptions options)
    {
      var registered = Core.Register(kernel, driverId, options);
      if (registered)
        EnsureHooksRegistered();
      return registered;
    }

    internal static void Unregister(FluentDockerKernel kernel, string driverId, string sessionId)
    {
      Core.Unregister(kernel, driverId, sessionId);
    }

    private static void EnsureHooksRegistered()
    {
      if (Interlocked.Exchange(ref _registered, 1) != 0)
        return;

      AppDomain.CurrentDomain.ProcessExit += (_, _) => Core.RunCleanup();
      try
      {
        // ponytail: SIGKILL/hard host termination cannot be caught; next-run label reaping reclaims STOPPED leaks, and running leaks only via the opt-in FLUENTDOCKER_REAP_RUNNING_AFTER age ceiling.
        _sigIntRegistration = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnPosixSignal);
        _sigTermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnPosixSignal);
      }
      catch (PlatformNotSupportedException)
      {
      }
    }

    /// <summary>Runs best-effort cleanup for one POSIX termination signal.</summary>
    /// <remarks>
    /// The first SIGINT/SIGTERM runs cleanup synchronously; a second signal during
    /// the capped cleanup window is intentionally unhandled.
    /// </remarks>
    private static void OnPosixSignal(PosixSignalContext context)
    {
      Core.RunCleanup();
    }
  }
}
