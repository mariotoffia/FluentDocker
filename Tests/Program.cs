using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Extensions;
using System;
using System.Diagnostics;

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
            RunPsCloneStdOut();
            //RunContainer();
        }
        // http://csharptest.net/532/using-processstart-to-capture-console-output/index.html
        static void RunPsCloneStdOut()
        {
            ProcessExecutor.Run(s => Console.WriteLine(s), null,
                @"C:\windows\system32\windowspowershell\v1.0\powershell.exe"/*, "-NoLogo -NoExit -Command docker ps"*/);            
            
        }
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
