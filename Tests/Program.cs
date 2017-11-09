using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Extensions;
using System;

namespace Tests
{
    class Program
    {
        static void Main(string[] args)
        {
            var hosts = new Hosts().Discover();
            Console.WriteLine($"Number of hosts:{hosts.Count}");
            foreach(var host in hosts)
            {
                Console.WriteLine($"{host.Host} {host.Name} {host.State}");
            }
            Console.WriteLine("Spinning up a postgres and wait for ready state...");
            using (
                var container =
                new Builder().UseContainer()
                    .UseImage("postgres:9.6-alpine")
                    .ExposePort(5432)
                    .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
                    .WaitForPort("5432/tcp", 30000 /*30s*/)
                    .Build()
                    .Start())
            {
                var config = container.GetConfiguration(true);
                if (ServiceRunningState.Running == config.State.ToServiceState())
                {
                    Console.WriteLine("Service is running");
                }
                else
                {
                    Console.WriteLine("Failed to start nginx instance...");
                }
            }  
        }
    }
}
