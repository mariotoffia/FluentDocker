using System;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Builders;

namespace Simple
{
  internal class ExecuteCommandInRunningContainer
  {
    internal static void Runner()
    {
      using (
          var container =
              new Builder().UseContainer()
                  .UseImage("postgres:9.6-alpine")
                  .ExposePort(5432)
                  .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
                  .WaitForPort("5432/tcp", 30000)
                  .Build()
                  .Start())
      {

        var config = container.GetConfiguration(true);

        // Run the *echo* command inside the running container
        var output = container.DockerHost.Execute(
                                      config.Id,
                                      "echo \"I'm inside the container\"",
                                      container.Certificates);

        if (output.Data.Contains("I'm inside the container"))
        {
          Console.WriteLine("The command was executed successfully");
        }
        else
        {
          Console.WriteLine("The command was not executed successfully");
        }

      }
    }
  }
}
