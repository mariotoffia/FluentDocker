using System.Threading.Tasks;
using FluentDocker.Builders.V3;

namespace FluentDocker.MsTest
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
                .WithPort("5432", null);
        }

        protected override async Task OnContainerInitializedAsync()
        {
            await Task.Delay(5000);

            var containerInfo = await Container.InspectAsync();
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
