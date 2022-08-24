using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Compose;
using Ductus.FluentDocker.Model.Images;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Extensions;
using Ductus.FluentDocker.Services.Impl;

namespace Ductus.FluentDocker.Builders
{
  [Experimental(TargetVersion = "3.0.0")]
  public sealed class CompositeBuilder : BaseBuilder<ICompositeService>
  {
    private readonly DockerComposeFileConfig _config = new DockerComposeFileConfig();

    internal CompositeBuilder(IBuilder parent, string composeFile = null) : base(parent)
    {
      if (!string.IsNullOrEmpty(composeFile))
        _config.ComposeFilePath.Add(composeFile);
    }

    public override ICompositeService Build()
    {
      if (_config.ComposeFilePath.Count == 0)
        throw new FluentDockerException("Cannot create service without a docker-compose file");

      var host = FindHostService();
      if (!host.HasValue)
        throw new FluentDockerException(
          $"Cannot build service using compose-file(s) {string.Join(", ", _config.ComposeFilePath)} since no host service is defined");

      var container = new DockerComposeCompositeService(host.Value, _config);

      AddHooks(container);

      return container;
    }

    private void AddHooks(ICompositeService container)
    {
      IContainerService Resolve(string name)
      {
        return container.Containers.FirstOrDefault(x => x.Name == name);
      }

      foreach (var config in _config.ContainerConfiguration.Values)
      {
        // Copy files just before starting
        if (null != config.CpToOnStart)
          container.AddHook(ServiceRunningState.Starting,
            service =>
            {
              Fd.DisposeOnException(svc =>
              {
                foreach (var copy in config.CpToOnStart)
                  Resolve(config.Name)?.CopyTo(copy.Item2, copy.Item1);
              }, service, "Copy on Start");
            });

        // Wait for port when started
        if (null != config.WaitForPort)
          container.AddHook(ServiceRunningState.Running,
            service =>
            {
              Fd.DisposeOnException(svc =>
                  Resolve(config.Name)?.WaitForPort(config.WaitForPort.Item1, config.WaitForPort.Item3,
                    config.WaitForPort.Item2),
                service, "Wait for Port");
            });

        // Wait for http when started
        if (null != config.WaitForHttp && 0 != config.WaitForHttp.Count)
          container.AddHook(ServiceRunningState.Running, service =>
          {
            Fd.DisposeOnException(svc =>
            {
              foreach (var prm in config.WaitForHttp)
                Resolve(config.Name)?.WaitForHttp(prm.Url, prm.Timeout, prm.Continuation, prm.Method, prm.ContentType,
                  prm.Body);
            }, service, "Wait for HTTP");
          });

        // Wait for lambda when started
        if (null != config.WaitLambda && 0 != config.WaitLambda.Count)
          container.AddHook(ServiceRunningState.Running, service =>
          {
            Fd.DisposeOnException(svc =>
            {
              foreach (var continuation in config.WaitLambda)
                Resolve(config.Name)?.Wait(continuation);
            }, service, "Wait for Lambda");
          });

        // Wait for process when started
        if (null != config.WaitForProcess)
          container.AddHook(ServiceRunningState.Running,
            service =>
            {
              Fd.DisposeOnException(svc =>
                  Resolve(config.Name)?.WaitForProcess(config.WaitForProcess.Item1, config.WaitForProcess.Item2),
                service, "Wait for Process");
            });

        // docker execute on running
        if (null != config.ExecuteOnRunningArguments && config.ExecuteOnRunningArguments.Count > 0)
          container.AddHook(ServiceRunningState.Running, service =>
          {
            Fd.DisposeOnException(svc =>
            {
              var csvc = Resolve(config.Name);
              if (null == csvc)
                return;

              foreach (var binaryAndArguments in config.ExecuteOnRunningArguments)
              {
                var result = csvc.DockerHost.Execute(csvc.Id, binaryAndArguments, csvc.Certificates);
                if (!result.Success)
                  throw new FluentDockerException($"Failed to execute {binaryAndArguments} error: {result.Error}");
              }
            }, service, "Execute on Running Argument");
          });

        // Copy files / folders on dispose
        if (null != config.CpFromOnDispose && 0 != config.CpFromOnDispose.Count)
          container.AddHook(ServiceRunningState.Removing, service =>
          {
            Fd.DisposeOnException(svc =>
            {
              foreach (var copy in config.CpFromOnDispose)
                Resolve(config.Name)?.CopyFrom(copy.Item2, copy.Item1);
            }, service, "Copy from on Dispose");
          });

        // docker execute when disposing
        if (null != config.ExecuteOnDisposingArguments && config.ExecuteOnDisposingArguments.Count > 0)
          container.AddHook(ServiceRunningState.Removing, service =>
          {
            Fd.DisposeOnException(svc =>
            {
              var csvc = Resolve(config.Name);
              if (null == svc)
                return;

              foreach (var binaryAndArguments in config.ExecuteOnDisposingArguments)
              {
                var result = csvc.DockerHost.Execute(csvc.Id, binaryAndArguments, csvc.Certificates);
                if (!result.Success)
                  throw new FluentDockerException($"Failed to execute {binaryAndArguments} error: {result.Error}");
              }
            }, service, "Execute on Disposing Argument");
          });

        // Export container on dispose
        if (null != config.ExportOnDispose)
          container.AddHook(ServiceRunningState.Removing, service =>
          {
            Fd.DisposeOnException(svc =>
            {
              var csvc = Resolve(config.Name);
              if (null == csvc)
                return;

              if (config.ExportOnDispose.Item3(csvc))
                csvc.Export(config.ExportOnDispose.Item1, config.ExportOnDispose.Item2);
            }, service, "Export on Dispose");
          });
      }
    }

    public CompositeBuilder FromFile(params string[] composeFile)
    {
      ((List<string>)_config.ComposeFilePath).AddRange(composeFile);
      return this;
    }

    /// <summary>
    /// Explicitly sets the project directory.
    /// </summary>
    /// <param name="projectDir">The project dir, if none set it to an empty string.</param>
    /// <returns>Itself for fluent access.</returns>
    public CompositeBuilder UseProjectDir(TemplateString projectDir)
    {
      _config.ProjectDirectory = projectDir;
      return this;
    }

    public CompositeBuilder AlwaysPull()
    {
      _config.AlwaysPull = true;
      return this;
    }

    public CompositeBuilder ForceRecreate()
    {
      _config.ForceRecreate = true;
      return this;
    }

    public CompositeBuilder NoRecreate()
    {
      _config.NoRecreate = true;
      return this;
    }

    public CompositeBuilder NoBuild()
    {
      _config.NoBuild = true;
      return this;
    }

    public CompositeBuilder ForceBuild()
    {
      _config.ForceBuild = true;
      return this;
    }

    public CompositeBuilder Timeout(TimeSpan timeoutInSeconds)
    {
      _config.TimeoutSeconds = timeoutInSeconds;
      return this;
    }

    public CompositeBuilder RemoveOrphans()
    {
      _config.RemoveOrphans = true;
      return this;
    }

    public CompositeBuilder ServiceName(string name)
    {
      _config.AlternativeServiceName = name;
      return this;
    }

    public CompositeBuilder UseColor()
    {
      _config.UseColor = true;
      return this;
    }

    public CompositeBuilder KeepVolumes()
    {
      _config.KeepVolumes = true;
      return this;
    }

    public CompositeBuilder RemoveAllImages()
    {
      _config.ImageRemoval = ImageRemovalOption.All;
      return this;
    }

    public CompositeBuilder RemoveNonTaggedImages()
    {
      _config.ImageRemoval = ImageRemovalOption.Local;
      return this;
    }


    public CompositeBuilder KeepRunning()
    {
      _config.StopOnDispose = false;
      _config.KeepContainers = true;
      return this;
    }

    public CompositeBuilder KeepContainer()
    {
      _config.KeepContainers = true;
      return this;
    }

    /// <summary>
    /// Sets environment variables when executing docker
    /// compose. Those may be used in a docker-compose file
    /// to pass dynamic information such as image labels etc.
    /// </summary>
    /// <param name="nameValue">An array of name=value string. It is possible to have equal sign
    /// in the value area since it will use the first encountered equal sign as the environment variable
    /// name and the value.</param>
    /// <returns>Itself for fluent access.</returns>
    public CompositeBuilder WithEnvironment(params string[] nameValue)
    {
      if (null == nameValue || 0 == nameValue.Length)
      {
        return this;
      }

      foreach (var nv in nameValue)
      {
        var env = nv.Extract();
        if (null == env || string.IsNullOrWhiteSpace(env.Item1))
          continue;
        _config.EnvironmentNameValue.Add(env.Item1, env.Item2 ?? string.Empty);
      }

      return this;
    }


    /// <summary>
    /// Kept for backward compatibility, will be removed in 3.0.0.
    /// </summary>
    /// <returns>Itself for fluent access.</returns>
    public CompositeBuilder KeepOnDispose()
    {
      return KeepContainer();
    }

    public CompositeBuilder ExportOnDispose(string service, string hostPath,
      Func<IContainerService, bool> condition = null)
    {
      GetContainerSpecificConfig(service).ExportOnDispose =
        new Tuple<TemplateString, bool, Func<IContainerService, bool>>(hostPath.EscapePath(), false /*no-explode*/,
          condition ?? (svc => true));
      return this;
    }

    [Obsolete("Please use the properly spelled `ExportExplodedOnDispose` method instead.")]
    public CompositeBuilder ExportExploadedOnDispose(string service, string hostPath,
      Func<IContainerService, bool> condition = null)
      => ExportExplodedOnDispose(service, hostPath, condition);

    public CompositeBuilder ExportExplodedOnDispose(string service, string hostPath,
      Func<IContainerService, bool> condition = null)
    {
      GetContainerSpecificConfig(service).ExportOnDispose =
        new Tuple<TemplateString, bool, Func<IContainerService, bool>>(hostPath.EscapePath(), true /*explode*/,
          condition ?? (svc => true));
      return this;
    }

    public CompositeBuilder CopyOnStart(string service, string hostPath, string containerPath)
    {
      var config = GetContainerSpecificConfig(service);
      if (null == config.CpToOnStart)
        config.CpToOnStart = new List<Tuple<TemplateString, TemplateString>>();

      config.CpToOnStart.Add(
        new Tuple<TemplateString, TemplateString>(hostPath.EscapePath(), containerPath.EscapePath()));
      return this;
    }

    public CompositeBuilder CopyOnDispose(string service, string containerPath, string hostPath)
    {
      var config = GetContainerSpecificConfig(service);
      if (null == config.CpFromOnDispose)
        config.CpFromOnDispose = new List<Tuple<TemplateString, TemplateString>>();

      config.CpFromOnDispose.Add(
        new Tuple<TemplateString, TemplateString>(hostPath.EscapePath(), containerPath.EscapePath()));
      return this;
    }

    public CompositeBuilder WaitForPort(string service, string portAndProto, long millisTimeout = long.MaxValue, string address = null)
    {
      GetContainerSpecificConfig(service).WaitForPort =
        new Tuple<string, string, long>(portAndProto, address, millisTimeout);
      return this;
    }

    public CompositeBuilder WaitForProcess(string service, string process, long millisTimeout = long.MaxValue)
    {
      GetContainerSpecificConfig(service).WaitForProcess = new Tuple<string, long>(process, millisTimeout);
      return this;
    }

    /// <summary>
    /// Custom function to do verification if wait is over or not.
    /// </summary>
    /// <param name="service">The service to attach this wait on.</param>
    /// <param name="continuation">The continuation lambda.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    /// It is possible to stack multiple lambdas, they are executed in order they where registered (per service).
    /// The lambda do the actual action to determine if the wait is over or not. If it returns zero or less, the
    /// wait is over. If it returns a positive value, the wait function will wait this amount of milliseconds before
    /// invoking it again. The second argument is the invocation count. This can be used for the function to determine
    /// any type of abort action due to the amount of invocations. If continuation wishes to abort, it shall throw
    /// <see cref="FluentDockerException"/>.
    /// </remarks>
    public CompositeBuilder Wait(string service, Func<IContainerService, int, int> continuation)
    {
      GetContainerSpecificConfig(service).WaitLambda.Add(continuation);
      return this;
    }

    /// <summary>
    ///   Executes one or more commands including their arguments when container has started.
    /// </summary>
    /// <param name="service">The service to execute on</param>
    /// <param name="execute">The binary to execute including any arguments to pass to the binary.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    ///   Each execute string is respected as a binary and argument.
    /// </remarks>
    public CompositeBuilder ExecuteOnRunning(string service, params string[] execute)
    {
      var config = GetContainerSpecificConfig(service);
      if (null == config.ExecuteOnRunningArguments)
        config.ExecuteOnRunningArguments = new List<string>();

      config.ExecuteOnRunningArguments.AddRange(execute);
      return this;
    }

    /// <summary>
    ///   Executes one or more commands including their arguments when container about to stop.
    /// </summary>
    /// <param name="service">The service to execute on</param>
    /// <param name="execute">The binary to execute including any arguments to pass to the binary.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    ///   Each execute string is respected as a binary and argument.
    /// </remarks>
    public CompositeBuilder ExecuteOnDisposing(string service, params string[] execute)
    {
      var config = GetContainerSpecificConfig(service);
      if (null == config.ExecuteOnDisposingArguments)
        config.ExecuteOnDisposingArguments = new List<string>();

      config.ExecuteOnDisposingArguments.AddRange(execute);
      return this;
    }

    /// <summary>
    ///   Waits for a request to be passed or failed.
    /// </summary>
    /// <param name="service">The service to attach to.</param>
    /// <param name="url">The url including any query parameters.</param>
    /// <param name="timeout">The amount of time to wait before failing.</param>
    /// <param name="continuation">Optional continuation that evaluates if it shall still wait or continue.</param>
    /// <param name="method">Optional. The method. Default is <see cref="HttpMethod.Get" />.</param>
    /// <param name="contentType">Optional. The content type in put, post operations. Defaults to application/json</param>
    /// <param name="body">Optional. A body to post or put.</param>
    /// <returns>The response body in form of a string.</returns>
    /// <exception cref="ArgumentException">If <paramref name="method" /> is not GET, PUT, POST or DELETE.</exception>
    /// <exception cref="HttpRequestException">If any errors during the HTTP request.</exception>
    public CompositeBuilder WaitForHttp(string service, string url, long timeout = 60_000,
      Func<RequestResponse, int, long> continuation = null, HttpMethod method = null,
      string contentType = "application/json", string body = null)
    {
      var config = GetContainerSpecificConfig(service);
      config.WaitForHttp.Add(new ContainerSpecificConfig.WaitForHttpParams
      {
        Url = url,
        Timeout = timeout,
        Continuation = continuation,
        Method = method,
        ContentType = contentType,
        Body = body
      });

      return this;
    }

    private ContainerSpecificConfig GetContainerSpecificConfig(string service)
    {
      if (_config.ContainerConfiguration.TryGetValue(service, out var config))
        return config;

      config = new ContainerSpecificConfig { Name = service };
      _config.ContainerConfiguration.Add(service, config);

      return config;
    }

    protected override IBuilder InternalCreate()
    {
      return new CompositeBuilder(this);
    }
  }
}
