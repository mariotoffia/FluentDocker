// TODO: https://www.docker.com/blog/how-to-deploy-on-remote-docker-hosts-with-docker-compose/

using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Services;

public class DockerContextAwareRemoteBuilder : BaseBuilder<IHostService>
{
    private string _uri;
    internal DockerContextAwareRemoteBuilder(IBuilder parent, string uri = null) : base(parent) {
        _uri = uri;
    }

    public override IHostService Build()
    {
        throw new System.NotImplementedException();
    }

    protected override IBuilder InternalCreate()
    {
        throw new System.NotImplementedException();
    }
}