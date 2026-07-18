using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Drivers.Docker.Api.Connection
{
  public sealed partial class DockerApiConnection
  {
    /// <summary>Highest Docker Engine API version this client intentionally targets.</summary>
    internal const string MaxSupportedApiVersion = "1.45";
    private const string MinSupportedApiVersion = "1.24";

    private async Task NegotiateApiVersionAsync(CancellationToken ct)
    {
      try
      {
        using var negotiationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        negotiationCts.CancelAfter(_config.ConnectionTimeout);
        using var ping = await _httpClient.GetAsync("/_ping", negotiationCts.Token)
            .ConfigureAwait(false);
        if (!ping.IsSuccessStatusCode)
        {
          PinDefaultApiVersion();
          return;
        }

        var daemonMax = HeaderValue(ping, "API-Version");
        var versionInfo = await TryGetVersionInfoAsync(negotiationCts.Token).ConfigureAwait(false);
        daemonMax ??= versionInfo.ApiVersion;
        daemonMax ??= MaxSupportedApiVersion;

        EnsureSupportedDaemonVersion(daemonMax, versionInfo.MinApiVersion);
        _negotiation = new NegotiationState(
            MinVersion(daemonMax, MaxSupportedApiVersion), Negotiated: true);
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        throw;
      }
      catch (DriverException)
      {
        // Unsupported-daemon-version: typed, non-transient, deliberately rethrown (the
        // request helpers map it to CommandResponse.Fail; GetVersionedPathAsync negatively
        // caches it for a cooldown).
        throw;
      }
      catch (Exception ex)
      {
        PinDefaultApiVersion();
        _logger.LogWarning(ex,
            "Docker API version negotiation failed; using client default {ApiVersion} for this request (will retry)",
            MaxSupportedApiVersion);
      }
    }

    private static string HeaderValue(HttpResponseMessage response, string name)
    {
      return response.Headers.TryGetValues(name, out var values)
          ? values.FirstOrDefault()
          : null;
    }

    private async Task<(string ApiVersion, string MinApiVersion)> TryGetVersionInfoAsync(
        CancellationToken ct)
    {
      try
      {
        using var response = await _httpClient.GetAsync("/version", ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
          return default;
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        return (
            doc.RootElement.GetStringOrDefault("ApiVersion"),
            doc.RootElement.GetStringOrDefault("MinAPIVersion"));
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        _logger.LogDebug(ex, "Docker API /version lookup failed during negotiation");
        return default;
      }
    }

    private void PinDefaultApiVersion()
    {
      // Pin the client default as the version for the CURRENT request (never send unversioned),
      // but leave Negotiated:false so the next request re-negotiates. This honors the retry
      // contract in GetVersionedPathAsync and lets the client recover if the daemon later comes
      // up as an OLDER version than our max (otherwise it would be permanently stuck at the pin).
      _negotiation = new NegotiationState(MaxSupportedApiVersion, Negotiated: false);
    }

    private static void EnsureSupportedDaemonVersion(string daemonMax, string daemonMin)
    {
      // Typed DriverException (not a raw InvalidOperationException) so CommandResponse-returning
      // driver methods surface this as a Fail with a dedicated error code instead of leaking a
      // raw exception through the contract.
      if (CompareVersion(daemonMax, MinSupportedApiVersion) < 0)
        throw new DriverException(
            $"Docker daemon API version {daemonMax} is too old; need >= {MinSupportedApiVersion}.",
            ErrorCodes.Api.UnsupportedVersion, isTransient: false);

      if (!string.IsNullOrEmpty(daemonMin) &&
          CompareVersion(daemonMin, MaxSupportedApiVersion) > 0)
        throw new DriverException(
            $"Docker daemon requires API version >= {daemonMin}; client supports <= {MaxSupportedApiVersion}.",
            ErrorCodes.Api.UnsupportedVersion, isTransient: false);
    }

    private static string MinVersion(string left, string right) =>
        CompareVersion(left, right) <= 0 ? left : right;

    private static int CompareVersion(string left, string right) =>
        Version.Parse(left).CompareTo(Version.Parse(right));
  }
}
