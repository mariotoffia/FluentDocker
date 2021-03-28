using System.Linq;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Builders
{
  public sealed class HostBuilder : BaseBuilder<IHostService>
  {
    private IHostService customHostService;

    internal HostBuilder(IBuilder builder) : base(builder)
    {
    }

    public override IHostService Build()
    {

      if (this.customHostService != null) {
        return this.customHostService;
      }

      return IsNative ? new Hosts().Native() : null;

    }

    protected override IBuilder InternalCreate()
    {
      return new HostBuilder(this);
    }

    public bool IsNative { get; private set; }

    public HostBuilder UseNative()
    {
      IsNative = true;
      return this;
    }

    public HostBuilder UseHost(IHostService customHostService) {
      this.customHostService = customHostService;
      return this;
    }

   /// <summary>
    /// Creates a `IHostService` based on a _URI_.
    /// </summary>
    /// <param name="uri">The _URI_ to the docker daemon.</param>
    /// <param name="name">An optional name. If none is specified the _URI_ is the name.</param>
    /// <param name="isNative">If the docker daemon is native or not. Default to true.</param>
    /// <param name="stopWhenDisposed">If it should be stopped when disposed, default to false.</param>
    /// <param name="isWindowsHost">If it is a docker daemon that controls windows containers or not. Default false.</param>
    /// <param name="certificatePath">
    /// Optional path to where certificates are located in order to do TLS communication with docker daemon. If not provided,
    /// it will try to get it from the environment _DOCKER_CERT_PATH_.
    /// </param>
    /// <returns>Itself for fluent access.</returns>
     public HostBuilder FromUri(
      DockerUri uri,
      string name = null,
      bool isNative = true,
      bool stopWhenDisposed = false,
      bool isWindowsHost = false,
      string certificatePath = null)
    {
      this.customHostService = new Hosts().FromUri(uri,name,isNative,stopWhenDisposed,isWindowsHost,certificatePath);
      return this;
    }

    public MachineBuilder UseMachine()
    {
      var existing = Childs.FirstOrDefault(x => x is MachineBuilder);
      if (null != existing)
      {
        return (MachineBuilder)existing;
      }

      var builder = new MachineBuilder(this);
      Childs.Add(builder);
      return builder;
    }

    public RemoteSshHostBuilder UseSsh(string ipAddress = null)
    {
      var builder = new RemoteSshHostBuilder(this, ipAddress);
      Childs.Add(builder);
      return builder;
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

    public NetworkBuilder UseNetwork(string name = null)
    {
      var builder = new NetworkBuilder(this, name);
      Childs.Add(builder);
      return builder;
    }

    public VolumeBuilder UseVolume(string name = null)
    {
      var builder = new VolumeBuilder(this, name);
      Childs.Add(builder);
      return builder;
    }
  }
}
