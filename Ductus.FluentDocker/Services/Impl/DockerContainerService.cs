namespace Ductus.FluentDocker.Services.Impl
{
  public sealed class DockerContainerService : ServiceBase, IContainerService
  {
    private readonly bool _stopOnDispose;
    private readonly bool _removeOnDispose;

    public DockerContainerService(string name, bool stopOnDispose = false, bool removeOnDispose = false) : base(name)
    {
      _stopOnDispose = stopOnDispose;
      _removeOnDispose = removeOnDispose;
    }

    public override void Dispose()
    {
    }

    public override void Start()
    {
    }

    public string Id { get; }
  }
}
