using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Components.Parsing;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// HTTP/API adapter implementing model management via DMR's native
  /// <c>/models*</c> endpoints (pull/list/inspect/delete) — an HTTP path that does
  /// not need the <c>docker</c> binary on PATH. Tag/push/package/prune/disk-usage
  /// have no native endpoints and throw <see cref="NotSupportedException"/>
  /// (the composite runner prefers the CLI management adapter for those).
  /// </summary>
  public class DockerApiModelManagementDriver : IModelManagementDriver
  {
    private const string Unsupported =
        "This operation has no native /models endpoint; use the CLI management driver.";

    private readonly IModelApiConnection _connection;

    /// <summary>Initializes the driver.</summary>
    /// <param name="connection">The HTTP connection (Docker socket / model runner).</param>
    public DockerApiModelManagementDriver(IModelApiConnection connection) => _connection = connection ?? throw new ArgumentNullException(nameof(connection));

    /// <inheritdoc />
    public async Task<CommandResponse<ModelInfo>> PullAsync(DriverContext context,
        ModelReference model, IProgress<ModelPullProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var json = JsonHelper.Serialize(new Dictionary<string, string> { ["from"] = model.ToString() });
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        await using (var stream = await _connection.PostStreamAsync("/models/create", content, cancellationToken).ConfigureAwait(false))
        {
          using var reader = new StreamReader(stream, Encoding.UTF8);
          string line;
          while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
          {
            if (string.IsNullOrWhiteSpace(line))
              continue;

            var update = ModelJsonParser.ParseNativePullProgress(line);
            if (update != null)
              progress?.Report(update);
          }
        }

        var info = await InspectAsync(context, model, cancellationToken).ConfigureAwait(false);
        if (info.Success)
          return info;

        return CommandResponse<ModelInfo>.Fail(info.Error ?? "model pull failed", ErrorCodes.Model.PullFailed, info.ErrorContext, info.ExitCode);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<ModelInfo>.Fail(ex.Message, ErrorCodes.Model.PullFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<ModelInfo>>> ListAsync(DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        using var response = await _connection.GetAsync("/models", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
          return CommandResponse<IList<ModelInfo>>.Fail(
              await SafeReadError(response, cancellationToken).ConfigureAwait(false),
              ErrorCodes.Model.ListFailed,
              CreateApiErrorContext(context, "ListModels", response),
              (int)response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!ModelJsonParser.TryParseList(body, out var models))
          return CommandResponse<IList<ModelInfo>>.Fail(
              "Unable to parse model list response",
              ErrorCodes.Model.ListFailed,
              CreateApiErrorContext(context, "ListModels", response),
              (int)response.StatusCode);

        return CommandResponse<IList<ModelInfo>>.Ok(models);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<IList<ModelInfo>>.Fail(ex.Message, ErrorCodes.Model.ListFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ModelInfo>> InspectAsync(DriverContext context,
        ModelReference model, CancellationToken cancellationToken = default)
    {
      try
      {
        using var response = await _connection.GetAsync(ModelApiPaths.ForModel(model), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
          return CommandResponse<ModelInfo>.Fail(
              await SafeReadError(response, cancellationToken).ConfigureAwait(false),
              response.StatusCode == System.Net.HttpStatusCode.NotFound ? ErrorCodes.Model.NotFound : ErrorCodes.Model.InspectFailed,
              CreateApiErrorContext(context, "InspectModel", response),
              (int)response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var info = ModelJsonParser.ParseInfo(body);
        return info != null
            ? CommandResponse<ModelInfo>.Ok(info)
            : CommandResponse<ModelInfo>.Fail("Unable to parse model inspect response", ErrorCodes.Model.InspectFailed);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<ModelInfo>.Fail(ex.Message, ErrorCodes.Model.InspectFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RemoveAsync(DriverContext context,
        ModelReference model, bool force = false, CancellationToken cancellationToken = default)
    {
      try
      {
        using var response = await _connection.DeleteAsync(ModelApiPaths.ForModel(model), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
          return CommandResponse<Unit>.Fail(
              await SafeReadError(response, cancellationToken).ConfigureAwait(false),
              ErrorCodes.Model.RemoveFailed,
              CreateApiErrorContext(context, "RemoveModel", response),
              (int)response.StatusCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Model.RemoveFailed);
      }
    }

    /// <inheritdoc />
    public Task<CommandResponse<Unit>> TagAsync(DriverContext context,
        ModelReference source, ModelReference target, CancellationToken cancellationToken = default) =>
        Task.FromException<CommandResponse<Unit>>(new NotSupportedException(Unsupported));

    /// <inheritdoc />
    public Task<CommandResponse<Unit>> PushAsync(DriverContext context,
        ModelReference model, CancellationToken cancellationToken = default) =>
        Task.FromException<CommandResponse<Unit>>(new NotSupportedException(Unsupported));

    /// <inheritdoc />
    public Task<CommandResponse<ModelInfo>> PackageAsync(DriverContext context,
        ModelPackageRequest request, CancellationToken cancellationToken = default) =>
        Task.FromException<CommandResponse<ModelInfo>>(new NotSupportedException(Unsupported));

    /// <inheritdoc />
    public Task<CommandResponse<ModelPruneResult>> PurgeAllAsync(DriverContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromException<CommandResponse<ModelPruneResult>>(new NotSupportedException(Unsupported));

    /// <inheritdoc />
    public Task<CommandResponse<ModelDiskUsage>> DiskUsageAsync(DriverContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromException<CommandResponse<ModelDiskUsage>>(new NotSupportedException(Unsupported));

    private static async Task<string> SafeReadError(HttpResponseMessage response, CancellationToken cancellationToken)
    {
      try
      {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)response.StatusCode}" : body;
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        // Caller cancellation must propagate, not be masked as a generic HTTP error.
        throw;
      }
      catch (Exception)
      {
        return $"HTTP {(int)response.StatusCode}";
      }
    }

    private static ErrorContext CreateApiErrorContext(DriverContext context, string operation, HttpResponseMessage response) =>
        new(operation)
        {
          DriverId = context?.DriverId,
          Host = context?.Host,
          ExitCode = (int)response.StatusCode
        };
  }
}
