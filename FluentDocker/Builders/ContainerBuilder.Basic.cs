using System;
using System.Globalization;
using FluentDocker.Services;

namespace FluentDocker.Builders
{
  internal sealed partial class ContainerBuilder
  {
    internal bool KeepContainerRequested => _keepContainer;
    internal IServiceAsync PendingService => _pendingService;

    internal void ResetForRetry()
    {
      // Retry contract: every BuildAsync attempt starts with clean per-attempt state.
      _pendingService = null;
      _waitConditionsExecuted = false;
    }

    public IContainerBuilder WithPort(string hostPort, string containerPort)
    {
      var normalized = NormalizeContainerPort(containerPort);
      if (_ports.TryGetValue(normalized, out var existing) &&
          !string.Equals(existing, hostPort, StringComparison.Ordinal))
        _duplicateContainerPorts.Add(normalized);
      _ports[normalized] = hostPort;
      return this;
    }

    public IContainerBuilder ExposePort(string containerPort)
    {
      var normalized = NormalizeContainerPort(containerPort);
      if (_ports.TryGetValue(normalized, out var existing) &&
          !string.Equals(existing, string.Empty, StringComparison.Ordinal))
        _duplicateContainerPorts.Add(normalized);
      _ports[normalized] = "";
      return this;
    }

    public IContainerBuilder ExposePort(int hostPort, int containerPort)
    {
      var normalized = NormalizeContainerPort(containerPort.ToString(CultureInfo.InvariantCulture));
      var host = hostPort.ToString(CultureInfo.InvariantCulture);
      if (_ports.TryGetValue(normalized, out var existing) &&
          !string.Equals(existing, host, StringComparison.Ordinal))
        _duplicateContainerPorts.Add(normalized);
      _ports[normalized] = host;
      return this;
    }

    public IContainerBuilder WithCommand(params string[] command) { _command.AddRange(command); return this; }
    public IContainerBuilder WithInteractive(bool interactive = true) { _interactive = interactive; return this; }
    public IContainerBuilder WithTty(bool tty = true) { _tty = tty; return this; }
    public IContainerBuilder WithEntrypoint(params string[] entrypoint) { _entrypoint = entrypoint; return this; }
    public IContainerBuilder WithVolume(string hostPath, string containerPath, bool isReadOnly = false)
    {
      ArgumentException.ThrowIfNullOrWhiteSpace(hostPath);
      ArgumentException.ThrowIfNullOrWhiteSpace(containerPath);
      if (containerPath.EndsWith(":ro", StringComparison.Ordinal)
          || containerPath.EndsWith(":rw", StringComparison.Ordinal))
      {
        throw new ArgumentException(
            "Do not append :ro/:rw to the container path; pass isReadOnly instead.",
            nameof(containerPath));
      }

      _volumes.Add($"{hostPath}:{containerPath}{(isReadOnly ? ":ro" : string.Empty)}");
      return this;
    }
  }
}
