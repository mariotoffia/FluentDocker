using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Services.Extensions;

namespace Ductus.FluentDocker.MsTest
{
  public abstract class PostgresTestBase : FluentDockerTestBase
  {
    protected const string PostgresConnectionString =
      "Server={0};Port={1};Userid={2};Password={3};" +
      "Pooling=false;MinPoolSize=1;MaxPoolSize=20;" +
      "Timeout=15;SslMode=Disable;Database={4}";

    protected const string PostgresUser = "postgres";
    protected const string PostgresDb = "postgres";
    protected readonly string DockerImage;
    protected readonly string PostgresPassword;

    protected string ConnectionString;

    protected PostgresTestBase(string password = "mysecretpassword", string image = "postgres:latest")
    {
      PostgresPassword = password;
      DockerImage = image;
    }

    protected override ContainerBuilder Build()
    {
      return new Builder().UseContainer()
        .UseImage("postgres:latest")
        .WithEnvironment($"POSTGRES_PASSWORD={PostgresPassword}")
        .ExposePort(5432)
        .WaitForPort("5432/tcp", 30000 /*30s*/);
    }

    protected override void OnContainerInitialized()
    {
      var ep = Container.ToHostExposedEndpoint("5432/tcp");
      ConnectionString = string.Format(PostgresConnectionString, ep.Address, ep.Port, PostgresUser,
        PostgresPassword, PostgresDb);
    }
  }
}