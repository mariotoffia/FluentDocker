using System.Threading.Tasks;
using FluentDocker.Builders;

namespace FluentDocker.XUnit
{
  /// <summary>
  /// Base class for PostgreSQL integration tests.
  /// </summary>
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

    protected PostgresTestBase(string password = "mysecretpassword", string image = "postgres:alpine")
    {
      PostgresPassword = password;
      DockerImage = image;
    }

    protected override void ConfigureContainer(IContainerBuilder builder)
    {
      builder
          .UseImage(DockerImage)
          .WithEnvironment("POSTGRES_PASSWORD", PostgresPassword)
          .WithEnvironment("POSTGRES_HOST_AUTH_METHOD", "trust")
          .WithPort("5432", null); // Expose to random host port
    }

    protected override async Task OnContainerInitializedAsync()
    {
      // Wait for PostgreSQL to be ready
      await Task.Delay(5000);

      // Get the container info to find the exposed port
      var containerInfo = await Container.InspectAsync();

      // Default to port 5432 if we can't get the exposed port
      var port = "5432";
      var host = "localhost";

      ConnectionString = string.Format(
          PostgresConnectionString,
          host,
          port,
          PostgresUser,
          PostgresPassword,
          PostgresDb);
    }
  }
}
