using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ductus.FluentDocker.Internal;

namespace Ductus.FluentDocker
{
  public class DockerBuilder
  {
    private const string DefaultDockerUrl = "https://192.168.99.100:2376";

    private const string DockerHost = "DOCKER_HOST";
    private const string DockerCertPath = "DOCKER_CERT_PATH";
    private const string DockerMachineName = "DOCKER_MACHINE_NAME";
    private const string DockerToolboxInstallPath = "DOCKER_TOOLBOX_INSTALL_PATH";

    private readonly DockerParams _prms = new DockerParams();

    public DockerBuilder()
    {
      var env = Environment.GetEnvironmentVariables();
      _prms.DockerCertPath = (string) env[DockerCertPath];
      _prms.DockerHost = env.Contains(DockerHost) ? (string) env[DockerHost] : DefaultDockerUrl;
      _prms.DockerMachineName = env.Contains(DockerMachineName) ? (string) env[DockerMachineName] : "default";
      _prms.DockerToolboxInstallPath = (string) env[DockerToolboxInstallPath];
    }

    /// <summary>
    ///   Specifies the host of the docker daemon.
    /// </summary>
    /// <param name="host">The host to communicate with.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    ///   This will override the default environment variable DOCKER_HOST for e.g. not set or
    ///   if remote docker daemon communication is wanted.
    /// </remarks>
    public DockerBuilder UseHost(string host)
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

    /// <summary>
    ///   The image to run as a <see cref="DockerContainer" />.
    /// </summary>
    /// <param name="name">
    ///   The name of the image including the version. If no version is specified it will use
    ///   'imgname':latest.
    /// </param>
    /// <returns>Itself for fluent access.</returns>
    public DockerBuilder WithImage(string name)
    {
      if (-1 == name.IndexOf(':'))
      {
        name += ":latest";
      }

      _prms.ImageName = name;
      return this;
    }

    /// <summary>
    ///   Sets a explicit container name. It must be unique. Two running containers can not
    ///   not have the same name.
    /// </summary>
    /// <param name="name">The unique name of the container.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    ///   It is handy to reference the container by name instead of the default hash. Then it is
    ///   possible to e.g. do 'docker stop kalle' (if named kalle).
    /// </remarks>
    public DockerBuilder ContainerName(string name)
    {
      _prms.ContainerName = name;
      return this;
    }

    /// <summary>
    ///   Specifiec / overrides the default user in the container to be a specific one.
    /// </summary>
    /// <param name="user">The user to execute as in the container.</param>
    /// <returns>Itself for fluent access.</returns>
    public DockerBuilder AsUser(string user)
    {
      _prms.User = user;
      return this;
    }

    /// <summary>
    ///   Sets environment strings in the docker container e.g. 'POSTGRES_PASSWORD=kalle'.
    /// </summary>
    /// <param name="nameValuePair">Name and value pair to be set as environment variable.</param>
    /// <returns></returns>
    public DockerBuilder WithEnvironment(params string[] nameValuePair)
    {
      _prms.Env = nameValuePair;
      return this;
    }

    /// <summary>
    ///   Waits for a port to be exposed.
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

    /// <summary>
    ///   Command to be executed when the container is started.
    /// </summary>
    /// <param name="command">The command and the following arguments (if any).</param>
    /// <returns>Itself for fluent access.</returns>
    public DockerBuilder RunCommand(params string[] command)
    {
      _prms.Cmd = command;
      return this;
    }

    /// <summary>
    ///   Mounts volumes on the following format 'local host path':'docker exposed volume':ro|rw where
    ///   'ro' is read-only and 'rw' is read write volume.
    /// </summary>
    /// <param name="volume">One or more volumes to mount to the host. See remarks around variables.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <exception cref="FluentDockerException">If incorrect volume string is encountered.</exception>
    /// <remarks>
    ///   If omitting the 'ro' or 'rw' it will mount the volume read-write by default.
    ///   It is possible to adress the standard windows temp directory and random generate a directory name by
    ///   '${TEMP}' and '${RND}'. If the host folder do not exists it will be created prior creating the container.
    /// </remarks>
    public DockerBuilder MountVolumes(params string[] volume)
    {
      if (null == volume || 0 == volume.Length)
      {
        return this;
      }

      _prms.Volumes = DockerVolumeMount.ToMount(volume).ToArray();
      return this;
    }

    /// <summary>
    ///   Builds a <see cref="DockerContainer" /> that can be <see cref="DockerContainer.Create" />d
    ///   and <see cref="DockerContainer.Start" />ed.
    /// </summary>
    /// <param name="startImmediately">
    ///   If set to true (default is false) it will create and start the container before
    ///   returning the container.
    /// </param>
    /// <returns>A <see cref="DockerContainer" /> configured with specified configuration.</returns>
    /// <remarks>
    ///   If <paramref name="startImmediately" /> is set to true and the container image is not present in the local docker
    ///   repository, it will pull the image before create and start the container.
    /// </remarks>
    public DockerContainer Build(bool startImmediately = false)
    {
      var container = new DockerContainer(_prms);
      return startImmediately ? container.Start() : container;
    }
  }
}