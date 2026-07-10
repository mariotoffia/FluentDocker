using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Builders
{
  /// <summary>
  /// ContainerBuilder partial: execute pipeline.
  /// </summary>
  internal sealed partial class ContainerBuilder
  {
    #region Execute

    public Task<IServiceAsync> ExecuteAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(TimeSpan.FromSeconds(120), cancellationToken);

    internal async Task<IServiceAsync> ExecuteAsync(
        TimeSpan cleanupTimeout, CancellationToken cancellationToken)
    {
      Validate();
      var driver = _kernel.SysCtl<Drivers.IContainerDriver>(_driverId);
      _kernel.TrySysCtl<Drivers.IImageDriver>(_driverId, out var imageDriver);
      var context = new DriverContext(_driverId);

      // Authenticate to a private registry before any image pull (force-pull or the implicit daemon
      // pull on create), so WithRegistryAuth is a fluent alternative to hand-resolving IAuthDriver
      // (TST-MAJ-5).
      if (_registryAuth != null)
      {
        if (!_kernel.TrySysCtl<Drivers.IAuthDriver>(_driverId, out var authDriver))
          throw new FluentDockerException("WithRegistryAuth() requires a driver that supports IAuthDriver.");
        var login = await authDriver.LoginAsync(context, _registryAuth, cancellationToken).ConfigureAwait(false);
        if (!login.Success)
          throw new DriverException(
              $"Registry login failed for '{_registryAuth.Server ?? "Docker Hub"}': {login.Error}",
              login.ErrorCode, login.ErrorContext);
      }

      if (!string.IsNullOrEmpty(_name) && _existsBehavior != ContainerExistsBehavior.Default)
      {
        var existing = await FindExistingContainerAsync(driver, context, _name, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
          if (_existsBehavior == ContainerExistsBehavior.Reuse)
          {
            _reusedExisting = true;
            LogQueuedConfigIgnoredForReuse();
            var reuseService = new Services.Impl.ContainerService(
                _kernel, _driverId, existing, _image, _name,
                false, false,
                _deleteVolumeOnDispose, _deleteNamedVolumeOnDispose,
                _customResolver, _lifecycleHooks);

            var inspectResult = await driver.InspectAsync(context, existing, cancellationToken).ConfigureAwait(false);
            if (inspectResult.Success && inspectResult.Data?.State?.Running != true)
            {
              await StartReusedContainerAsync(driver, context, existing, reuseService, cancellationToken).ConfigureAwait(false);
              _pendingService = reuseService;
              _waitConditionsExecuted = true;
              await RunPostStartAsync(reuseService, cancellationToken).ConfigureAwait(false);
            }
            else
            {
              await reuseService.InspectAsync(cancellationToken).ConfigureAwait(false);
              _pendingService = reuseService;
              _waitConditionsExecuted = true;
              // Borrowed running container: verify readiness (wait conditions) but do NOT re-run
              // start hooks (CopyToOnStart/ExecuteOnRunning). Those fire only when THIS build starts
              // the container; re-running them on an already-running container would repeat side
              // effects (e.g. seed/migration commands).
              await ExecuteWaitConditionsAsync(reuseService, cancellationToken).ConfigureAwait(false);
              if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug(
                    "Reusing running container '{Name}'; wait conditions verified. Start hooks " +
                    "(CopyToOnStart/ExecuteOnRunning) and configuration differences are not applied.",
                    _name);
            }

            return reuseService;
          }
          else if (_existsBehavior == ContainerExistsBehavior.Destroy)
          {
            var remove = await driver.RemoveAsync(context, existing,
                _destroyForce, _destroyRemoveVolumes, cancellationToken).ConfigureAwait(false);
            if (!remove.Success)
              throw new DriverException($"Failed to remove existing container '{_name}': {remove.Error}",
                  remove.ErrorCode, remove.ErrorContext);
          }
        }
      }
      if (_forcePullImage && imageDriver == null)
        throw new FluentDockerException("ForcePullImage() requires a driver that supports IImageDriver.");
      if (_forcePullImage)
        await ExecuteForcePullAsync(imageDriver, context, cancellationToken).ConfigureAwait(false);

      var config = new Drivers.ContainerCreateConfig
      {
        Image = _image,
        Name = _name,
        Environment = _environment,
        PortBindings = _ports,
        Command = _command.Count > 0 ? [.. _command] : null,
        Labels = _labels.Count > 0 ? _labels : null,
        Volumes = _volumes.Count > 0 ? _volumes : null,
        Networks = _networks.Count > 0 ? _networks : null,
        WorkingDirectory = _workingDir,
        User = _user,
        RestartPolicy = _restartPolicy,
        Hostname = _hostname,
        ExtraHosts = _extraHosts,
        NetworkMode = _networkMode,
        Ipv4Address = _ipv4Address,
        Ipv6Address = _ipv6Address,
        MemoryLimit = _memoryLimit,
        CpuShares = _cpuShares,
        Privileged = _privileged,
        AutoRemove = _autoRemove,
        Links = _links.Count > 0
              ? [.. _links.Select(l => l.Alias != l.ContainerName
                  ? $"{l.ContainerName}:{l.Alias}" : l.ContainerName)]
              : null,
        NetworkAliases = _networkAliases.Count > 0
              ? _networkAliases
                  .GroupBy(a => a.NetworkName)
                  .ToDictionary(g => g.Key, g => g.Select(a => a.Alias).ToList())
              : null,
        Pod = _pod,
        CapAdd = _capAdd.Count > 0 ? _capAdd : null,
        CapDrop = _capDrop.Count > 0 ? _capDrop : null,
        SecurityOpt = _securityOpt.Count > 0 ? _securityOpt : null,
        ShmSize = _shmSize,
        Tmpfs = _tmpfs.Count > 0 ? _tmpfs : null,
        Devices = _devices.Count > 0 ? _devices : null,
        ReadonlyRootfs = _readonlyRootfs,
        Platform = _platform,
        Runtime = _runtime,
        Interactive = _interactive,
        Tty = _tty,
        Entrypoint = _entrypoint?.Length > 0 ? _entrypoint : null,
        StopSignal = _stopSignal,
        HealthCheck = _healthCheck,
        Dns = _dns.Count > 0 ? _dns : null
      };

      var response = await driver.CreateAsync(context, config, cancellationToken).ConfigureAwait(false);
      if (!response.Success)
        throw new DriverException($"Failed to create container: {response.Error}",
            response.ErrorCode, response.ErrorContext);

      var service = new Services.Impl.ContainerService(
          _kernel, _driverId, response.Data.Id, _image, _name,
          !_keepRunning, !_keepContainer,
          _deleteVolumeOnDispose, _deleteNamedVolumeOnDispose,
          _customResolver, _lifecycleHooks);

      _pendingService = service;
      var hasLinks = _links.Count > 0;
      // Capture deferred-start intent at EXECUTE time. Reuse branches return before this point,
      // so a reused (already-existing) container is never re-started by the deferred link pass.
      _startDeferred = hasLinks;

      if (!hasLinks)
      {
        try
        {
          await service.StartAsync(cancellationToken).ConfigureAwait(false);
          await WaitForContainerStartedAsync(
              driver, context, response.Data.Id, _name, AllowCleanExitOnStart,
              StartupTimeoutMs, StartupPollIntervalMs, cancellationToken).ConfigureAwait(false);
          _waitConditionsExecuted = true;
          await RunPostStartAsync(service, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Container build failed");
          _waitConditionsExecuted = false;

          var logTail = ex is OperationCanceledException
              ? null
              : await ReadLogTailAsync(driver, context, response.Data.Id, cancellationToken).ConfigureAwait(false);
          try
          {
            if (!_keepContainer)
            {
              using var cleanupCts = new CancellationTokenSource(cleanupTimeout);
              await service.RemoveAsync(force: true, removeVolumes: true, cleanupCts.Token).ConfigureAwait(false);
            }
          }
          catch (Exception cleanupEx)
          {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
              _logger.LogDebug(
                  cleanupEx,
                  "Best-effort cleanup failed for container '{ContainerId}' after build failure.",
                  response.Data.Id);
            }
          }
          if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            throw;
          if (!string.IsNullOrWhiteSpace(logTail))
            ex.Data["ContainerLogTail"] = logTail;
          if (ex.GetType() == typeof(FluentDockerException))
            throw new FluentDockerException(AppendLogTail(ex.Message, logTail), ex);
          throw;
        }
      }

      return service;
    }

    private bool HasQueuedContainerConfig() =>
        _environment.Count > 0 || _ports.Count > 0 || _volumes.Count > 0;

    private void LogQueuedConfigIgnoredForReuse()
    {
      if (!HasQueuedContainerConfig())
        return;

      _logger.LogWarning(
          "Existing container '{Name}' is reused as-is; queued env, ports, and volumes are not applied.",
          _name);
    }

    #endregion
  }
}
