using System;
using Ductus.FluentDocker.Internal;

namespace Ductus.FluentDocker
{
  public class DockerBuilder
  {
    private const string DefaultDockerUrl = "https://192.168.99.100:2376";

    // ReSharper disable once InconsistentNaming
    private const string DOCKER_HOST = "DOCKER_HOST";
    private const string DockerCertPath = "DOCKER_CERT_PATH";
    private const string DockerMachineName = "DOCKER_MACHINE_NAME";
    private const string DockerToolboxInstallPath = "DOCKER_TOOLBOX_INSTALL_PATH";

    private readonly DockerParams _prms = new DockerParams();

    public DockerBuilder()
    {
      var env = Environment.GetEnvironmentVariables();
      _prms.DockerCertPath = (string) env[DockerCertPath];
      _prms.DockerHost = env.Contains(DOCKER_HOST) ? (string) env[DOCKER_HOST] : DefaultDockerUrl;
      _prms.DockerMachineName = env.Contains(DockerMachineName) ? (string) env[DockerMachineName] : "default";
      _prms.DockerToolboxInstallPath = (string) env[DockerToolboxInstallPath];
    }

    public DockerBuilder DockerHost(string host)
    {
      _prms.DockerHost = host;
      return this;
    }

    /// <summary>
    ///   List the container ports that you would like to open in the container.
    /// </summary>
    /// <param name="ports">The ports to expose from the container.</param>
    /// <returns>Itself for fluent access.</returns>
    public DockerBuilder ExposePorts(params string[] ports)
    {
      _prms.Ports = ports;
      return this;
    }

    public DockerBuilder WithImage(string name)
    {
      if (-1 == name.IndexOf(':'))
      {
        name += ":latest";
      }

      _prms.ImageName = name;
      return this;
    }

    public DockerBuilder ContainerName(string name)
    {
      _prms.ContainerName = name;
      return this;
    }

    public DockerBuilder AsUser(string user)
    {
      _prms.User = user;
      return this;
    }

    public DockerBuilder WithEnvironment(params string[] nameValuePair)
    {
      _prms.Env = nameValuePair;
      return this;
    }

    /// <summary>
    /// Waits for a port to be exposed.
    /// </summary>
    /// <param name="port">The docker port to wait for e.g. 'tcp/5833'</param>
    /// <param name="millisTimeout">Number of millis to wait until fail.</param>
    /// <returns>Itself for fluent access.</returns>
    public DockerBuilder WaitForPort(string port, long millisTimeout = long.MaxValue)
    {
      _prms.WaitTimeout = millisTimeout;
      _prms.PortToWaitOn = port;
      return this;
    }

    public DockerContainer Build(bool startImmediately = false)
    {
      var container = new DockerContainer(_prms);
      return startImmediately ? container.Start() : container;
    }
  }
}