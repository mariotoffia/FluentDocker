using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Services;
using System;
using System.Diagnostics;
using Ductus.FluentDocker.Executors;

namespace Tests
{
    class Program
    {
        static void Main(string[] args)
        {
            //SudoMechanism.Password.SetSudo("<my-sudo-password>");
            //SudoMechanism.NoPassword.SetSudo();
            //SudoMechanism.None.SetSudo();
            //RunPs();
            //RunPsCloneStdOut();
            //RunContainer();
        }
        // https://docs.microsoft.com/en-us/dotnet/standard/exceptions/how-to-use-finally-blocks
        
        // http://csharptest.net/532/using-processstart-to-capture-console-output/index.html
        static void RunPs()
        {
            var process = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = @"C:\windows\system32\windowspowershell\v1.0\powershell.exe", 
                    Arguments = "-NoLogo -NoExit -Command docker ps", 
                    RedirectStandardOutput = false, 
                    UseShellExecute = true, 
                    CreateNoWindow = false,
                }
            };
            process.Start();
        }

        private static void RunContainer()
        {
            var hosts = new Hosts().Discover();
            Console.WriteLine($"Number of hosts:{hosts.Count}");
            foreach (var host in hosts)
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
