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
        var args = BuildComposeArgs(config) + " " + BuildListSubArgs(config);

        var result = await ExecuteCommandAsync(args, cancellationToken);

        if (!result.Success)
          return CommandResponse<IList<ComposeServiceInfo>>.Fail(
              result.Error ?? "Compose ps failed", ErrorCodes.Compose.ListFailed);

        return CommandResponse<IList<ComposeServiceInfo>>.Ok(
            ParseServiceList(result.Output));
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
        var args = BuildComposeArgs(config) + " " + BuildLogsSubArgs(config);
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
        var args = BuildComposeArgs(config) + " " + BuildConfigSubArgs(config);

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
        var output = result.Output.Trim();

        // Docker Compose V2 may return a JSON array or NDJSON
        if (output.StartsWith("["))
        {
          try
          {
            var arr = JsonConvert.DeserializeObject<List<ComposeImage>>(output);
            if (arr != null)
              images.AddRange(arr);
          }
          catch { }
        }
        else
        {
          var lines = output.Split(
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
            $" port --protocol {config.Protocol} {config.Service} {config.PrivatePort}";
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
        var args = BuildComposeArgs(config) + " " + BuildBuildSubArgs(config);
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
        var args = BuildComposeArgs(config) + " " + BuildPullSubArgs(config);
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
        var args = BuildComposeArgs(config) + " " + BuildRunSubArgs(config);

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
        var args = BuildComposeArgs(config) + " " + BuildScaleSubArgs(config);

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
        var args = BuildComposeArgs(config) + " " + BuildCreateSubArgs(config);
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
