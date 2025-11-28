using System;
using System.Linq;
using FluentDocker.Commands;
using FluentDocker.Services;

namespace DockerInDockerLinux
{
    class Program
    {
        static void Main(string[] args)
        {
            var hosts = new Hosts().Discover();
                
            var docker = hosts.FirstOrDefault(x => x.IsNative) ?? hosts.FirstOrDefault(x => x.Name == "default");
            Console.WriteLine($"Docker host: {docker?.Host.Host}, {docker?.Host.AbsolutePath}, {docker?.Host.AbsoluteUri}");
            
            var containers = docker?.GetContainers();
            Console.WriteLine(docker?.Host.Host);
            Console.WriteLine($"Number of containers: {containers?.Count}");
        }
    }
}
