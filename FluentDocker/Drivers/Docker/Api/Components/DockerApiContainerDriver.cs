using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Api.ApiModels;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Container = FluentDocker.Model.Containers.Container;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Docker API implementation of IContainerDriver (lifecycle and information operations).
  /// Request building and JSON parsing helpers are in the Parsing partial file.
  /// Operations such as exec, copy, logs, diff, top, export, rename, and update
  /// are implemented in the separate Operations partial file.
  /// </summary>
  public partial class DockerApiContainerDriver : DockerApiDriverBase, IContainerDriver
  {
    public DockerApiContainerDriver(IDockerApiConnection connection) : base(connection) { }

    #region Lifecycle Operations

    /// <inheritdoc />
    public async Task<CommandResponse<ContainerCreateResult>> CreateAsync(
        DriverContext context, ContainerCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      var request = BuildCreateRequest(config);
      var path = "/containers/create";
      if (!string.IsNullOrEmpty(config.Name))
        path += $"?name={Uri.EscapeDataString(config.Name)}";

      var result = await PostJsonAsync(
          path, request,
          DockerApiJsonContext.Default.CreateContainerRequest,
          DockerApiJsonContext.Default.CreateContainerResponse,
          cancellationToken);
      if (!result.Success)
        return CommandResponse<ContainerCreateResult>.Fail(
            result.ErrorMessage,
            MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Container.CreateFailed),
            CreateErrorContext("POST /containers/create",
                result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<ContainerCreateResult>.Ok(new ContainerCreateResult
      {
        Id = result.Data?.Id,
        Name = config.Name,
        Warnings = result.Data?.Warnings ?? new List<string>()
      });
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ContainerRunResult>> RunAsync(
        DriverContext context, ContainerCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      var createResult = await CreateAsync(context, config, cancellationToken).ConfigureAwait(false);
      if (!createResult.Success)
        return CommandResponse<ContainerRunResult>.Fail(
            createResult.Error, createResult.ErrorCode,
            createResult.ErrorContext, createResult.ExitCode);

      var containerId = createResult.Data.Id;
      var startResult = await StartAsync(context, containerId, cancellationToken).ConfigureAwait(false);
      if (!startResult.Success)
        return CommandResponse<ContainerRunResult>.Fail(
            startResult.Error, startResult.ErrorCode,
            startResult.ErrorContext, startResult.ExitCode);

      return CommandResponse<ContainerRunResult>.Ok(new ContainerRunResult
      {
        Id = containerId,
        Warnings = createResult.Data.Warnings
      });
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> StartAsync(
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      var path = $"/containers/{Uri.EscapeDataString(containerId)}/start";
      var result = await PostAsync(path, null, cancellationToken).ConfigureAwait(false);
      return result.Success
          ? CommandResponse<Unit>.Ok(Unit.Default)
          : FailUnit(result, $"POST /containers/{containerId}/start", ErrorCodes.Container.StartFailed);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> StopAsync(
        DriverContext context, string containerId, int? timeout = null,
        CancellationToken cancellationToken = default)
    {
      var path = $"/containers/{Uri.EscapeDataString(containerId)}/stop";
      if (timeout.HasValue)
        path += $"?t={timeout.Value}";

      var result = await PostAsync(path, null, cancellationToken).ConfigureAwait(false);
      return result.Success
          ? CommandResponse<Unit>.Ok(Unit.Default)
          : FailUnit(result, $"POST /containers/{containerId}/stop", ErrorCodes.Container.StopFailed);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RestartAsync(
        DriverContext context, string containerId, int? timeout = null,
        CancellationToken cancellationToken = default)
    {
      var path = $"/containers/{Uri.EscapeDataString(containerId)}/restart";
      if (timeout.HasValue)
        path += $"?t={timeout.Value}";

      var result = await PostAsync(path, null, cancellationToken).ConfigureAwait(false);
      return result.Success
          ? CommandResponse<Unit>.Ok(Unit.Default)
          : FailUnit(result, $"POST /containers/{containerId}/restart", ErrorCodes.Container.RestartFailed);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PauseAsync(
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      var path = $"/containers/{Uri.EscapeDataString(containerId)}/pause";
      var result = await PostAsync(path, null, cancellationToken).ConfigureAwait(false);
      return result.Success
          ? CommandResponse<Unit>.Ok(Unit.Default)
          : FailUnit(result, $"POST /containers/{containerId}/pause", ErrorCodes.Container.PauseFailed);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> UnpauseAsync(
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      var path = $"/containers/{Uri.EscapeDataString(containerId)}/unpause";
      var result = await PostAsync(path, null, cancellationToken).ConfigureAwait(false);
      return result.Success
          ? CommandResponse<Unit>.Ok(Unit.Default)
          : FailUnit(result, $"POST /containers/{containerId}/unpause", ErrorCodes.Container.UnpauseFailed);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> KillAsync(
        DriverContext context, string containerId, string signal = "SIGKILL",
        CancellationToken cancellationToken = default)
    {
      var path = $"/containers/{Uri.EscapeDataString(containerId)}/kill";
      if (!string.IsNullOrEmpty(signal))
        path += $"?signal={Uri.EscapeDataString(signal)}";

      var result = await PostAsync(path, null, cancellationToken).ConfigureAwait(false);
      return result.Success
          ? CommandResponse<Unit>.Ok(Unit.Default)
          : FailUnit(result, $"POST /containers/{containerId}/kill", ErrorCodes.Container.KillFailed);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context, string containerId,
        bool force = false, bool removeVolumes = false,
        CancellationToken cancellationToken = default)
    {
      var id = Uri.EscapeDataString(containerId);
      var path = $"/containers/{id}?force={Fmt(force)}&v={Fmt(removeVolumes)}";

      var result = await DeleteAsync(path, cancellationToken).ConfigureAwait(false);
      return result.Success
          ? CommandResponse<Unit>.Ok(Unit.Default)
          : FailUnit(result, $"DELETE /containers/{containerId}", ErrorCodes.Container.RemoveFailed);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ContainerWaitResult>> WaitAsync(
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      var path = $"/containers/{Uri.EscapeDataString(containerId)}/wait";
      var result = await PostJsonAsync(
          path, DockerApiJsonContext.Default.WaitContainerResponse, cancellationToken);
      if (!result.Success)
        return CommandResponse<ContainerWaitResult>.Fail(
            result.ErrorMessage,
            MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Container.NotFound),
            CreateErrorContext($"POST /containers/{containerId}/wait",
                result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<ContainerWaitResult>.Ok(new ContainerWaitResult
      {
        ExitCode = result.Data?.StatusCode ?? -1,
        Error = result.Data?.Error?.Message
      });
    }

    #endregion

    #region Information Operations

    /// <inheritdoc />
    public async Task<CommandResponse<Container>> InspectAsync(
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      var path = $"/containers/{Uri.EscapeDataString(containerId)}/json";
      var result = await GetJsonElementAsync(path, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<Container>.Fail(
            result.ErrorMessage,
            MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Container.NotFound),
            CreateErrorContext($"GET /containers/{containerId}/json",
                result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<Container>.Ok(ParseContainerInspect(result.Data));
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<Container>>> ListAsync(
        DriverContext context, ContainerListFilter filter = null,
        CancellationToken cancellationToken = default)
    {
      var path = BuildListPath(filter);
      var result = await GetJsonElementAsync(path, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<IList<Container>>.Fail(
            result.ErrorMessage,
            MapHttpErrorCode(result.StatusCode),
            CreateErrorContext("GET /containers/json",
                result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<IList<Container>>.Ok(ParseContainerList(result.Data));
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ContainerStatsResult>> StatsAsync(
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      var path = $"/containers/{Uri.EscapeDataString(containerId)}" +
                 "/stats?stream=false";
      var result = await GetJsonElementAsync(path, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<ContainerStatsResult>.Fail(
            result.ErrorMessage,
            MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Container.NotFound),
            CreateErrorContext($"GET /containers/{containerId}/stats",
                result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<ContainerStatsResult>.Ok(
          ParseContainerStats(result.Data, containerId));
    }

    #endregion

    #region Helpers

    private CommandResponse<Unit> FailUnit(ApiResult result, string operation,
        string notFoundCode = null)
    {
      return CommandResponse<Unit>.Fail(
          result.ErrorMessage,
          MapNotFoundErrorCode(result.StatusCode, notFoundCode ?? ErrorCodes.Container.NotFound),
          CreateErrorContext(operation, result.StatusCode, result.ResponseBody),
          result.StatusCode);
    }

    private static string Fmt(bool value) => value ? "true" : "false";

    #endregion
  }
}
