using System;
using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Internal;

namespace Ductus.FluentDocker
{
  public class DockerBuilder
  {
    private const string DefaultDockerUrl = "https://192.168.99.100:2376";

    internal const string DockerHost = "DOCKER_HOST";
    internal const string DockerCertPath = "DOCKER_CERT_PATH";
    internal const string DockerMachineName = "DOCKER_MACHINE_NAME";
    internal const string DockerToolboxInstallPath = "DOCKER_TOOLBOX_INSTALL_PATH";
    internal const string DockerTlsVerify = "DOCKER_TLS_VERIFY";

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
    ///   When the container is about to be disposed.
    /// </summary>
    /// <returns>Fluent builder for disposal</returns>
    public WhenDisposedBuilder WhenDisposed()
    {
      return new WhenDisposedBuilder(this, _prms);
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
    public DockerBuilder UseDockerHost(string host)
    {
      _prms.DockerHost = host;
      return this;
    }

    /// <summary>
    ///   Copies a file or directory from the container onto the host.
    /// </summary>
    /// <param name="name">The name of the copy instruction. Can later be used to a lookup of the path.</param>
    /// <param name="containerPath">The path on the container to copy, either single file or subdirectory.</param>
    /// <param name="hostPath">The host path. Template arguments are accepted.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    ///   The <paramref name="name" /> is used to tag a copy instruction and the host path can later be resolved using
    ///   <see cref="DockerContainer.GetHostCopyPath" />.
    /// </remarks>
    public DockerBuilder CopyFromContainer(string containerPath, string hostPath, string name = null)
    {
      if (null == name)
      {
        name = Guid.NewGuid().ToString();
      }

      _prms.CopyFilesAfterStart.Add(new Tuple<string, string, string>(name, containerPath,
        hostPath.Render().ToPlatformPath()));

      return this;
    }

    /// <summary>
    ///   The the container host name.
    /// </summary>
    /// <param name="containerHostName">The host name that the container will be exposed as.</param>
    /// <returns>Itself for fluent access.</returns>
    public DockerBuilder UseHost(string containerHostName)
    {
      _prms.HostName = containerHostName;
      return this;
    }

    /// <summary>
    ///   Under which domain name shall the docker container be exposed as.
    /// </summary>
    /// <param name="containerDomain">The domain name</param>
    /// <returns>Itself for fluent access.</returns>
    public DockerBuilder UseDomain(string containerDomain)
    {
      _prms.DomainName = containerDomain;
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
    ///   Adds a link to another container from this one. The <paramref name="dnsName" /> will
    ///   be set in the /etc/hosts file in this container. It will point to the <paramref name="containerName" />
    ///   so it is e.g. possible to do "ping kalle" when a container link with kallecontainer1, kalle is set.
    /// </summary>
    /// <param name="containerName">The name of the named container to link to.</param>
    /// <param name="dnsName">The name of the entry in the /etc/hosts to point to the <paramref name="containerName" />.</param>
    /// <returns>Itself for fluent access.</returns>
    public DockerBuilder UseLink(string containerName, string dnsName)
    {
      if (string.IsNullOrEmpty(containerName))
      {
        throw new ArgumentException("Null or empty string is not allowed", nameof(containerName));
      }

      if (string.IsNullOrEmpty(containerName))
      {
        throw new ArgumentException("Null or empty string is not allowed", nameof(dnsName));
      }

      _prms.Links.Add($"{containerName}:{dnsName}");
      return this;
    }

    /// <summary>
    ///   Links two containers. Make sure that the container to be linked is started first.
    ///   <see cref="UseLink(string,string)" /> for more information.
    /// </summary>
    /// <param name="container">The builder where the <see cref="ContainerName" /> is taken.</param>
    /// <param name="dnsName">The name of the entry in the /etc/hosts to point to the <paramref name="container" /></param>
    /// <returns>Itself for fluent access.</returns>
    public DockerBuilder UseLink(DockerBuilder container, string dnsName)
    {
      return UseLink(container._prms.ContainerName, dnsName);
    }

    /// <summary>
    ///   Links two containers. Make sure that the container to be linked is started first.
    ///   <see cref="UseLink(string,string)" /> for more information.
    /// </summary>
    /// <param name="container">The container where the <see cref="DockerContainer.ContainerName" /> is taken.</param>
    /// <param name="dnsName">The name of the entry in the /etc/hosts to point to the <paramref name="container" /></param>
    /// <returns>Itself for fluent access.</returns>
    public DockerBuilder UseLink(DockerContainer container, string dnsName)
    {
      return UseLink(container.ContainerName, dnsName);
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
    ///   Waits for a specific process to start in the container before proceeding.
    /// </summary>
    /// <param name="process">The name of the process to wait for.</param>
    /// <param name="millisTimeout">The number of milliseconds to wait before failing. Default is infinite.</param>
    /// <returns>Itself for fluent access.</returns>
    public DockerBuilder WaitForProcess(string process, long millisTimeout = long.MaxValue)
    {
      _prms.ProcessWaitTimeout = millisTimeout;
      _prms.WaitForProcess = process;
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
      _prms.PortWaitTimeout = millisTimeout;
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
    ///   Adds a named volume that will be mounted. This name can be used to retrieve the mounted volumes
    ///   by name in the <see cref="DockerContainer" />. This is especially usefull when having template
    ///   parameters in the mount string.
    /// </summary>
    /// <param name="name">Name of the mount.</param>
    /// <param name="hostdir">The host directory to mount.</param>
    /// <param name="dockerdir">The directory in the container to mount.</param>
    /// <param name="mode">Either ro for read-only or rw for read-write mode.</param>
    /// <returns>Itelf for fluent access.</returns>
    /// <remarks>
    ///   See <see cref="MountVolumes" /> for template parameters and boot2docker issiues.
    /// </remarks>
    public DockerBuilder MountNamedVolume(string name, string hostdir, string dockerdir, string mode)
    {
      mode = string.IsNullOrEmpty(mode) ? "rw" : mode;

      var mount = DockerVolumeMount.ToMount($"{hostdir}:{dockerdir}:{mode}");
      mount.Name = name;

      if (null == _prms.Volumes)
      {
        _prms.Volumes = new[] {mount};
        return this;
      }

      _prms.Volumes = new List<DockerVolumeMount>(_prms.Volumes) {mount}.ToArray();
      return this;
    }

    /// <summary>
    ///   Mounts volumes on the following format 'host path':'inside docker container volume':ro|rw where
    ///   'ro' is read-only and 'rw' is read write volume.
    /// </summary>
    /// <param name="volume">One or more volumes to mount to the host. See remarks around variables.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <exception cref="FluentDockerException">If incorrect volume string is encountered.</exception>
    /// <remarks>
    ///   If omitting the 'ro' or 'rw' it will mount the volume read-write by default.
    ///   It is possible to adress the standard windows temp directory and random generate a directory name by
    ///   '${TEMP}' and '${RND}'. If the host folder do not exists it will be created prior creating the container.
    ///   Make sure that the host path has correct security and is reachable when in boot2docker. This is done for
    ///   the VM inside virtual box shares.
    /// </remarks>
    public DockerBuilder MountVolumes(params string[] volume)
    {
      if (null == volume || 0 == volume.Length)
      {
        return this;
      }

      if (null == _prms.Volumes)
      {
        _prms.Volumes = DockerVolumeMount.ToMount(volume).ToArray();
        return this;
      }

      var list = new List<DockerVolumeMount>(_prms.Volumes);
      list.AddRange(DockerVolumeMount.ToMount(volume));
      _prms.Volumes = list.ToArray();

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