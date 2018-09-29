using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Builders
{
  /// <summary>
  ///   Sets up a remote docker session in the docker-machine registry.
  /// </summary>
  /// <remarks>
  ///   Setup a SSH key for the remote host by: ssh-keygen -t rsa.
  ///   And then copy it to the remote machine:
  ///   ssh-copy-id {username}@{host}
  ///   Later, Docker Machine will be sending commands over SSH on our behalf, so you'll need to be able to enter sudo mode
  ///   without entering your password. You may want to only enable this while we configure Docker Machine. SSH to the remote
  ///   machine and edit the sudoers file: sudo nano /etc/sudoers
  ///   And add the following to the end of the file where {username} is your username on the remote machine:
  ///   {username}  ALL=(ALL) NOPASSWD:ALL
  ///   Save the file, logout and login again and you should be able to enter sudo mode without entering your password.
  ///   Docker Machine will SSH to the remote machine to configure the Docker engine. The Docker client will then connect on
  ///   TCP port 2376. You'll need to make sure this port is open on your firewall.
  ///   After docker is configured use this builder to set the ip, name and other options to connect to the remote machine.
  ///   This is only required once per host since it is stored in the docker-machine registry and this class will use the
  ///   name as the key to look it up.
  /// </remarks>
  public sealed class RemoteSshHostBuilder : BaseBuilder<IHostService>
  {
    private string _ipAddress;
    private string _name;
    private int _port = -1;
    private string _sshUser;

    private string _sshKeyPath = OperatingSystem.IsWindows()
      ? ((TemplateString) "${E_LOCALAPPDATA}/lxss/home/martoffi/.ssh/id_rsa").Rendered
      : "~/.ssh/id_rsa";
    
    internal RemoteSshHostBuilder(IBuilder parent, string ipAddress = null) : base(parent)
    {
      _ipAddress = ipAddress;
    }

    /// <summary>
    ///   Creates or looks up the named remote host using docker-machine registry.
    /// </summary>
    /// <returns>A host service if successful.</returns>
    /// <exception cref="FluentDockerException">If any errors occurs.</exception>
    /// <remarks>
    ///   This method first checks if a entry with the specified name already exist. If so it will return it without
    ///   checking the ip or other properties. If you want to update the entry please delete it first and then
    ///   use this method. If the entry is not found in the docker-machine registry. It will create it and discover it
    ///   again.
    /// </remarks>
    public override IHostService Build()
    {
      if (string.IsNullOrEmpty(_name))
        throw new FluentDockerException("Cannot create machine (for remote docker access) since no name is set");

      var machine = new Hosts().Discover().FirstOrDefault(x => x.Name == _name);
      if (null != machine) return machine;

      if (string.IsNullOrEmpty(_ipAddress))
        throw new FluentDockerException("Cannot create machine (for remote docker access) since no ip address is set");

      var opts = new List<string> {$"--generic-ip-address={_ipAddress}"};

      if (_port != -1) opts.Add($" --generic-ssh-port={_port}");
      if (!string.IsNullOrEmpty(_sshKeyPath)) opts.Add($"--generic-ssh-key=\"{_sshKeyPath}\"");
      if (!string.IsNullOrEmpty(_sshUser)) opts.Add($"--generic-ssh-user={_sshUser}");


      var resp = _name.Create("generic", opts.ToArray());
      if (!resp.Success)
        throw new FluentDockerException(
          $"Could not create machine (for remote docker host access) {_name} Log: {resp}");

      return new Hosts().Discover().FirstOrDefault(x => x.Name == _name);
    }

    /// <summary>
    ///   Name of the remote docker host to set or use to lookup.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    ///   If a docker-machine with specified name is already created, this will be used. If not
    ///   found it will create a machine with specified ip address and possibly other configuration before
    ///   returning the host on the <see cref="Build()" /> function.
    /// </remarks>
    public RemoteSshHostBuilder WithName(string name)
    {
      _name = name;
      return this;
    }

    /// <summary>
    ///   The remote daemon IP address.
    /// </summary>
    /// <param name="ipAddress">The address to use when communicating with the remote daemon using ssh.</param>
    /// <returns>Itself for fluent access.</returns>
    public RemoteSshHostBuilder UseIpAddress(string ipAddress)
    {
      _ipAddress = ipAddress;
      return this;
    }

    /// <summary>
    ///   If other than the default port (22) set it here.
    /// </summary>
    /// <param name="port">The port the remote SSH daemon is using.</param>
    /// <returns>Itself for fluent access.</returns>
    public RemoteSshHostBuilder UsePort(int port)
    {
      _port = port;
      return this;
    }

    /// <summary>
    ///   The fully qualified path to the key file to use when doing SSH authentication.
    /// </summary>
    /// <param name="path">The fully qualified path to the key file for SSH communication.</param>
    /// <returns>Itself for fluent access.</returns>
    public RemoteSshHostBuilder WithSshKeyPath(TemplateString path)
    {
      _sshKeyPath = OperatingSystem.IsWindows() ? path.Rendered.Replace('\\', '/') : path.Rendered;
      return this;
    }

    /// <summary>
    ///   The user (if other than root) to use when doing SSH communication.
    /// </summary>
    /// <param name="user">The user to use.</param>
    /// <returns>Itself for fluent access.</returns>
    public RemoteSshHostBuilder WithSshUser(string user)
    {
      _sshUser = user;
      return this;
    }

    public HostBuilder Host()
    {
      return (HostBuilder) ((IBuilder) this).Parent.Value;
    }

    public ImageBuilder DefineImage(string image = null)
    {
      var builder = new ImageBuilder(this).AsImageName(image);
      Childs.Add(builder);
      return builder;
    }

    public ContainerBuilder UseContainer()
    {
      var builder = new ContainerBuilder(this);
      Childs.Add(builder);
      return builder;
    }
    
    protected override IBuilder InternalCreate()
    {
      return new RemoteSshHostBuilder(this);
    }
  }
}