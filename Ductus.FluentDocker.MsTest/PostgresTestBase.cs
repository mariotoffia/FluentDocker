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

    protected override DockerBuilder Build()
    {
      return new DockerBuilder()
        .WithImage("postgres:latest")
        .WithEnvironment($"POSTGRES_PASSWORD={PostgresPassword}")
        .ExposePorts("5432")
        .WaitForPort("5432/tcp", 30000 /*30s*/);
    }

    protected override void OnContainerInitialized()
    {
      ConnectionString = string.Format(PostgresConnectionString, Container.Host,
        Container.GetHostPort("5432/tcp"), PostgresUser,
        PostgresPassword, PostgresDb);
    }
  }
}