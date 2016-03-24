namespace Ductus.FluentDocker.Internal
{
  internal class DockerParams
  {
    /// <summary>
    /// Command to execute in the container.
    /// </summary>
    internal string []Cmd;

    /// <summary>
    ///   Path to where docker has stored it's certificates e.g. 'C:\Users\mario\.docker\machine\machines\default'.
    /// </summary>
    internal string DockerCertPath;

    /// <summary>
    ///   The url to the docker host e.g. https://192.168.99.100:2376
    /// </summary>
    internal string DockerHost;

    /// <summary>
    ///   Name of the docker machine e.g. 'default'
    /// </summary>
    internal string DockerMachineName;

    /// <summary>
    ///   Path to where docker toolbox (if on mac or windows) is installed e.g. 'C:\Program Files\Docker Toolbox'.
    /// </summary>
    internal string DockerToolboxInstallPath;

    internal string DomainName;

    /// <summary>
    /// Environment strings.
    /// </summary>
    internal string[] Env;

    /// <summary>
    /// User to execute as in the container.
    /// </summary>
    internal string User;
    internal string HostName;
    internal string ImageName;
    internal string ContainerName;
    internal string[] Ports;
    internal string PortToWaitOn;
    internal DockerVolumeMount[] Volumes;
    internal long WaitTimeout;
  }
}