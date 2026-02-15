using System.Threading.Tasks;
using FluentDocker.Builders;

namespace FluentDocker.XUnit
{
  /// <summary>
  /// Base class for PostgreSQL integration tests.
  /// </summary>
  [System.Obsolete("Use an external plugin (FluentDocker.Testing.Plugin.Postgres) with " +
                    "FluentDocker.Testing.Core.ContainerResource instead. " +
                    "See docs/testing/migration-from-legacy.md for migration guide.")]
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
          .WithPort("5432", null) // Expose to random host port
          .WaitForPort("5432/tcp"); // Wait for PostgreSQL to accept connections
    }

    protected override async Task OnContainerInitializedAsync()
    {
      var containerInfo = await Container.InspectAsync();

      var host = "localhost";
      var port = "5432";

      const string portKey = "5432/tcp";
      if (containerInfo.NetworkSettings?.Ports != null
          && containerInfo.NetworkSettings.Ports.TryGetValue(portKey, out var bindings)
          && bindings is { Length: > 0 })
      {
        port = bindings[0].HostPort;
      }

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
