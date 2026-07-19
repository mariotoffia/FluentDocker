#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Docker API implementation of IServiceDriver for Docker Swarm services.
  /// Uses /services and /tasks endpoints.
  /// </summary>
  public partial class DockerApiServiceDriver(IDockerApiConnection connection) : DockerApiDriverBase(connection), IServiceDriver
  {
    /// <inheritdoc />
    public async Task<CommandResponse<ServiceCreateResult>> CreateAsync(
        DriverContext context, ServiceCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      var body = BuildServiceSpec(config);
      var result = await PostJsonElementAsync(
          "/services/create", body, DockerApiRegistryAuth.HeaderFor(Connection, config.Image),
          cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<ServiceCreateResult>.Fail(result.ErrorMessage,
            result.StatusCode is 599 or 408
                ? MapHttpErrorCode(result.StatusCode)
                : ErrorCodes.Service.CreateFailed,
            CreateErrorContext("POST /services/create", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<ServiceCreateResult>.Ok(new ServiceCreateResult
      {
        Id = result.Data.GetStringOrDefault("ID"),
        Warnings = result.Data.Prop("Warnings")?.ValueKind == JsonValueKind.Array
            ? result.Data.Prop("Warnings").Value.Deserialize<List<string>>()
            : []
      });
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context, string[] serviceIds,
        CancellationToken cancellationToken = default)
    {
      // Attempt every id (mirrors `docker service rm` and DockerApiSystemDriver.PruneAsync's
      // collect-then-fail) instead of aborting on the first failure, which would silently skip
      // removal of every id after the first 404 while the caller believes only one id failed.
      var failures = new List<string>();
      string firstFailCode = null;

      foreach (var id in serviceIds)
      {
        var result = await DeleteAsync(
            $"/services/{Uri.EscapeDataString(id)}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
          failures.Add($"{id}: {result.ErrorMessage}");
          firstFailCode ??= MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Service.NotFound);
        }
      }

      if (failures.Count > 0)
        return CommandResponse<Unit>.Fail(
            $"Failed to remove {failures.Count} of {serviceIds.Length} service(s): {string.Join("; ", failures)}",
            firstFailCode ?? ErrorCodes.Service.RemoveFailed,
            CreateErrorContext("DELETE /services/{id}", 0));

      return CommandResponse<Unit>.Ok(Unit.Default);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> UpdateAsync(
        DriverContext context, string serviceId, ServiceUpdateConfig config,
        CancellationToken cancellationToken = default)
    {
      var escapedId = Uri.EscapeDataString(serviceId);
      for (var attempt = 0; attempt < 2; attempt++)
      {
        var inspectResult = await InspectAsync(
            context, serviceId, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!inspectResult.Success)
          return CommandResponse<Unit>.Fail(inspectResult.Error, inspectResult.ErrorCode);

        var version = inspectResult.Data.Version;
        var body = BuildUpdateSpec(inspectResult.Data, config);
        var image = config.Image ?? inspectResult.Data.Image;
        var result = await PostAsync(
            $"/services/{escapedId}/update?version={Uri.EscapeDataString(version.ToString(CultureInfo.InvariantCulture))}",
            body, DockerApiRegistryAuth.HeaderFor(Connection, image), cancellationToken).ConfigureAwait(false);
        if (result.Success)
          return CommandResponse<Unit>.Ok(Unit.Default);

        if (attempt == 0 && IsVersionConflict(result))
          continue;

        return CommandResponse<Unit>.Fail(result.ErrorMessage,
            result.StatusCode is 599 or 408
                ? MapHttpErrorCode(result.StatusCode)
                : ErrorCodes.Service.UpdateFailed,
            CreateErrorContext($"POST /services/{serviceId}/update", result.StatusCode, result.ResponseBody),
            result.StatusCode);
      }

      return CommandResponse<Unit>.Fail(
          "Service update exhausted conflict retries",
          ErrorCodes.Service.UpdateFailed,
          CreateErrorContext($"POST /services/{serviceId}/update", 409),
          409);
    }

    private static bool IsVersionConflict(ApiResult result) =>
        result.StatusCode == 409 ||
        (result.StatusCode >= 500 &&
         (ContainsVersionConflict(result.ErrorMessage) ||
          ContainsVersionConflict(result.ResponseBody)));

    private static bool ContainsVersionConflict(string value) =>
        value?.Contains("update out of sequence", StringComparison.OrdinalIgnoreCase) == true;

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RollbackAsync(
        DriverContext context, string serviceId, bool detach = false,
        CancellationToken cancellationToken = default)
    {
      var inspectResult = await InspectAsync(context, serviceId, cancellationToken: cancellationToken).ConfigureAwait(false);
      if (!inspectResult.Success)
        return CommandResponse<Unit>.Fail(inspectResult.Error, inspectResult.ErrorCode);

      var version = inspectResult.Data.Version;
      var escapedId = Uri.EscapeDataString(serviceId);
      var result = await PostAsync(
          $"/services/{escapedId}/update?version={Uri.EscapeDataString(version.ToString(CultureInfo.InvariantCulture))}&rollback=previous",
          new { }, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<Unit>.Fail(result.ErrorMessage,
            ErrorCodes.Service.RollbackFailed,
            CreateErrorContext($"POST /services/{serviceId}/update (rollback)", result.StatusCode),
            result.StatusCode);

      // detach=false waits for the rolled-back spec to converge. Re-inspect to learn the (previous)
      // desired replica count; skip the wait for global mode / unknown counts.
      if (!detach)
      {
        var postInspect = await InspectAsync(context, serviceId, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (postInspect.Success && postInspect.Data.Replicas > 0)
        {
          var converged = await WaitForServiceConvergenceAsync(context, serviceId, postInspect.Data.Replicas, cancellationToken).ConfigureAwait(false);
          if (!converged.Success)
            return converged;
        }
      }

      return CommandResponse<Unit>.Ok(Unit.Default);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<ServiceInfo>>> ListAsync(
        DriverContext context, ServiceListFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
      var path = "/services";
      if (filter != null)
      {
        var filters = new Dictionary<string, List<string>>();
        if (!string.IsNullOrEmpty(filter.Name))
          filters["name"] = [filter.Name];
        if (!string.IsNullOrEmpty(filter.Id))
          filters["id"] = [filter.Id];
        if (!string.IsNullOrEmpty(filter.Mode))
          filters["mode"] = [filter.Mode];
        if (filters.Count > 0)
          path += $"?filters={Uri.EscapeDataString(JsonHelper.Serialize(filters))}";
      }

      var result = await GetJsonElementAsync(path, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<IList<ServiceInfo>>.Fail(result.ErrorMessage,
            ErrorCodes.Service.ListFailed,
            CreateErrorContext("GET /services", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var services = result.Data.ValueKind == JsonValueKind.Array
          ? result.Data.EnumerateArray().Select(ParseServiceInfo).ToList()
          : [];
      return CommandResponse<IList<ServiceInfo>>.Ok(services);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ServiceDetails>> InspectAsync(
        DriverContext context, string serviceId, bool pretty = false,
        CancellationToken cancellationToken = default)
    {
      var result = await GetJsonElementAsync(
          $"/services/{Uri.EscapeDataString(serviceId)}", cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<ServiceDetails>.Fail(result.ErrorMessage,
            MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Service.NotFound),
            CreateErrorContext($"GET /services/{serviceId}", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<ServiceDetails>.Ok(ParseServiceDetails(result.Data));
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<ServiceTask>>> GetTasksAsync(
        DriverContext context, string serviceId,
        ServiceTaskFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
      var filters = new Dictionary<string, List<string>> { ["service"] = [serviceId] };
      if (filter?.DesiredState != null)
        filters["desired-state"] = [filter.DesiredState];
      var path = $"/tasks?filters={Uri.EscapeDataString(JsonHelper.Serialize(filters))}";

      var result = await GetJsonElementAsync(path, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<IList<ServiceTask>>.Fail(result.ErrorMessage,
            ErrorCodes.Service.TasksFailed,
            CreateErrorContext("GET /tasks", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var tasks = result.Data.ValueKind == JsonValueKind.Array
          ? result.Data.EnumerateArray().Select(ParseServiceTask).ToList()
          : [];
      return CommandResponse<IList<ServiceTask>>.Ok(tasks);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<string>> GetLogsAsync(
        DriverContext context, string serviceId,
        ServiceLogsConfig? config = null,
        CancellationToken cancellationToken = default)
    {
      config ??= new ServiceLogsConfig();
      if (config.Follow)
        return CommandResponse<string>.Fail(
            "GetLogsAsync does not support follow=true because Docker API service logs can stream indefinitely.",
            ErrorCodes.Service.LogsFailed,
            CreateErrorContext($"GET /services/{serviceId}/logs", 0));

      var path = $"/services/{Uri.EscapeDataString(serviceId)}/logs?stdout=true&stderr=true";
      if (config.Tail.HasValue)
        path += $"&tail={config.Tail.Value}";
      if (config.Timestamps)
        path += "&timestamps=true";
      if (!string.IsNullOrEmpty(config.Since))
        path += $"&since={Uri.EscapeDataString(config.Since)}";

      try
      {
        using var stream = await GetRawStreamAsync(path, cancellationToken).ConfigureAwait(false);
        // No single container backs a service's aggregated log stream, so there is nothing to
        // TTY-inspect here; the pre-1.42 gate-failure path falls back to the byte-sniff.
        var logs = await ReadDockerLogTailAsync(stream, null, cancellationToken).ConfigureAwait(false);
        return CommandResponse<string>.Ok(logs);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(
            $"Failed to get logs for service '{serviceId}': {ex.Message}",
            ErrorCodes.Service.LogsFailed,
            CreateErrorContext($"GET /services/{serviceId}/logs", HttpStatusCodeOrZero(ex)));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> ScaleAsync(
        DriverContext context, Dictionary<string, int> serviceReplicas,
        bool detach = false, CancellationToken cancellationToken = default)
    {
      foreach (var (serviceId, replicas) in serviceReplicas)
      {
        var updateConfig = new ServiceUpdateConfig { Replicas = replicas };
        var result = await UpdateAsync(context, serviceId, updateConfig, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return result;

        // detach=false must wait for the service to actually converge (parity with the CLI driver).
        if (!detach)
        {
          var converged = await WaitForServiceConvergenceAsync(context, serviceId, replicas, cancellationToken).ConfigureAwait(false);
          if (!converged.Success)
            return converged;
        }
      }

      return CommandResponse<Unit>.Ok(Unit.Default);
    }
  }
}
