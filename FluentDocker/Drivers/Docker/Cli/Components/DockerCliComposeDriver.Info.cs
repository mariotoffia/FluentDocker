using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI compose driver: information, build/pull, execution, scale/copy, and create operations.
  /// </summary>
  public partial class DockerCliComposeDriver
  {
    #region Information Operations

    /// <inheritdoc />
    public async Task<CommandResponse<IList<ComposeServiceInfo>>> ListAsync(
        DriverContext context,
        ComposeListConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " ps --format json";
        if (config.All)
          args += " -a";
        if (config.Quiet)
          args += " -q";

        var result = await ExecuteCommandAsync(args, cancellationToken);

        if (!result.Success)
          return CommandResponse<IList<ComposeServiceInfo>>.Fail(
              result.Error ?? "Compose ps failed", ErrorCodes.Compose.ListFailed);

        var services = new List<ComposeServiceInfo>();
        var lines = result.Output.Split(
            new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
          try
          {
            var service = JsonConvert.DeserializeObject<ComposeServiceInfo>(line);
            if (service != null)
              services.Add(service);
          }
          catch { }
        }

        return CommandResponse<IList<ComposeServiceInfo>>.Ok(services);
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<ComposeServiceInfo>>.Fail(
            ex.Message, ErrorCodes.Compose.ListFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<string>> GetLogsAsync(
        DriverContext context,
        ComposeLogsConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " logs";
        if (config.Follow)
          args += " -f";
        if (config.Timestamps)
          args += " -t";
        if (config.Tail.HasValue)
          args += $" --tail {config.Tail.Value}";
        if (!string.IsNullOrEmpty(config.Since))
          args += $" --since {QuoteArgumentIfNeeded(config.Since)}";
        if (config.Services.Count > 0)
          args += " " + string.Join(" ", config.Services);

        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<string>.Ok(result.Output)
            : CommandResponse<string>.Fail(
                result.Error ?? "Compose logs failed", ErrorCodes.Compose.LogsFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, ErrorCodes.Compose.LogsFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<ComposeProcesses>>> TopAsync(
        DriverContext context,
        ComposeFileConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " top";
        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<IList<ComposeProcesses>>.Ok(ParseTopOutput(result.Output))
            : CommandResponse<IList<ComposeProcesses>>.Fail(
                result.Error ?? "Compose top failed", ErrorCodes.Compose.TopFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<ComposeProcesses>>.Fail(
            ex.Message, ErrorCodes.Compose.TopFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<string>> ConfigAsync(
        DriverContext context,
        ComposeConfigConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " config";
        if (config.ShowServices)
          args += " --services";
        if (config.ShowVolumes)
          args += " --volumes";

        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<string>.Ok(result.Output)
            : CommandResponse<string>.Fail(
                result.Error ?? "Compose config failed", ErrorCodes.Compose.ConfigFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, ErrorCodes.Compose.ConfigFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<ComposeImage>>> ImagesAsync(
        DriverContext context,
        ComposeFileConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " images --format json";
        var result = await ExecuteCommandAsync(args, cancellationToken);

        if (!result.Success)
          return CommandResponse<IList<ComposeImage>>.Fail(
              result.Error ?? "Compose images failed", ErrorCodes.Compose.ImagesFailed);

        var images = new List<ComposeImage>();
        var lines = result.Output.Split(
            new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
          try
          {
            var image = JsonConvert.DeserializeObject<ComposeImage>(line);
            if (image != null)
              images.Add(image);
          }
          catch { }
        }

        return CommandResponse<IList<ComposeImage>>.Ok(images);
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<ComposeImage>>.Fail(
            ex.Message, ErrorCodes.Compose.ImagesFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<string>> PortAsync(
        DriverContext context,
        ComposePortConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) +
            $" port {config.Service} {config.PrivatePort}/{config.Protocol}";
        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<string>.Ok(result.Output.Trim())
            : CommandResponse<string>.Fail(
                result.Error ?? "Compose port failed", ErrorCodes.Compose.PortFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, ErrorCodes.Compose.PortFailed);
      }
    }

    #endregion

    #region Build/Pull Operations

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> BuildAsync(
        DriverContext context,
        ComposeBuildConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " build";
        if (config.NoCache)
          args += " --no-cache";
        if (config.Pull)
          args += " --pull";
        if (config.ForceRm)
          args += " --force-rm";
        if (config.Parallel)
          args += " --parallel";
        if (config.Services.Count > 0)
          args += " " + string.Join(" ", config.Services);

        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                result.Error ?? "Compose build failed", ErrorCodes.Compose.BuildFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.BuildFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PullAsync(
        DriverContext context,
        ComposePullConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " pull";
        if (config.Quiet)
          args += " -q";
        if (config.IgnorePullFailures)
          args += " --ignore-pull-failures";
        if (config.Services.Count > 0)
          args += " " + string.Join(" ", config.Services);

        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                result.Error ?? "Compose pull failed", ErrorCodes.Compose.PullFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.PullFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PushAsync(
        DriverContext context,
        ComposeFileConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " push";
        if (config.Services.Count > 0)
          args += " " + string.Join(" ", config.Services);

        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                result.Error ?? "Compose push failed", ErrorCodes.Compose.PushFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.PushFailed);
      }
    }

    #endregion

    #region Execution Operations

    /// <inheritdoc />
    public async Task<CommandResponse<string>> ExecuteAsync(
        DriverContext context,
        ComposeExecConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " exec";
        if (config.Detach)
          args += " -d";
        if (!config.Tty)
          args += " -T";
        if (config.Privileged)
          args += " --privileged";
        if (!string.IsNullOrEmpty(config.User))
          args += $" -u {QuoteArgumentIfNeeded(config.User)}";
        if (!string.IsNullOrEmpty(config.WorkDir))
          args += $" -w {QuoteArgumentIfNeeded(config.WorkDir)}";
        if (config.Index.HasValue)
          args += $" --index {config.Index.Value}";
        args += $" {config.Service} " +
            string.Join(" ", config.Command ?? Array.Empty<string>());

        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<string>.Ok(result.Output)
            : CommandResponse<string>.Fail(
                result.Error ?? "Compose exec failed", ErrorCodes.Compose.ExecFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, ErrorCodes.Compose.ExecFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<string>> RunAsync(
        DriverContext context,
        ComposeRunConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " run";
        if (config.Detach)
          args += " -d";
        if (config.Rm)
          args += " --rm";
        if (config.NoDeps)
          args += " --no-deps";
        if (!string.IsNullOrEmpty(config.Name))
          args += $" --name {QuoteArgumentIfNeeded(config.Name)}";
        if (!string.IsNullOrEmpty(config.User))
          args += $" -u {QuoteArgumentIfNeeded(config.User)}";
        args += $" {config.Service}";
        if (config.Command != null && config.Command.Length > 0)
          args += " " + string.Join(" ", config.Command);

        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<string>.Ok(result.Output)
            : CommandResponse<string>.Fail(
                result.Error ?? "Compose run failed", ErrorCodes.Compose.RunFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, ErrorCodes.Compose.RunFailed);
      }
    }

    #endregion

    #region Scale/Copy Operations

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> ScaleAsync(
        DriverContext context,
        ComposeScaleConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " up -d --scale";
        foreach (var scale in config.Scale)
          args += $" {scale.Key}={scale.Value}";

        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                result.Error ?? "Compose scale failed", ErrorCodes.Compose.ScaleFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.ScaleFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> CopyAsync(
        DriverContext context,
        ComposeCopyConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " cp";
        if (config.Archive)
          args += " -a";
        if (config.FollowLinks)
          args += " -L";
        if (config.Index.HasValue)
          args += $" --index {config.Index.Value}";
        args += $" {QuoteArgumentIfNeeded(config.Source)} {QuoteArgumentIfNeeded(config.Destination)}";

        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                result.Error ?? "Compose cp failed", ErrorCodes.Compose.CopyFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.CopyFailed);
      }
    }

    #endregion

    #region Create Operations

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> CreateAsync(
        DriverContext context,
        ComposeCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " create";
        if (config.Build)
          args += " --build";
        if (config.ForceRecreate)
          args += " --force-recreate";
        if (config.NoRecreate)
          args += " --no-recreate";
        if (config.NoBuild)
          args += " --no-build";
        if (config.Services.Count > 0)
          args += " " + string.Join(" ", config.Services);

        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                result.Error ?? "Compose create failed", ErrorCodes.Compose.CreateFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.CreateFailed);
      }
    }

    #endregion

  }
}
