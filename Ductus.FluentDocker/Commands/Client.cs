using System;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Executors.Parsers;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model;

namespace Ductus.FluentDocker.Commands
{
  public static class Client
  {
    public static CommandResponse Ps(this Uri host, string options = "--quiet", string caCertPath = null,
      string clientCertPath = null,
      string clientKeyPath = null)
    {
      return
        new ProcessExecutor<ClientPsResponseParser, CommandResponse>(
          "docker".DockerPath(),
          $"{RenderBaseArgs(host, caCertPath, clientCertPath, clientKeyPath)} ps {options}").Execute();
    }

    public static string Run(this Uri host, string image, DockerRunArguments args = null, string caCertPath = null,
      string clientCertPath = null,
      string clientKeyPath = null)
    {
      var arg = $"{RenderBaseArgs(host, caCertPath, clientCertPath, clientKeyPath)} run -d";
      if (null != args)
      {
        arg += " " + args;
      }

      arg += " " + image;

      return
        new ProcessExecutor<SingleStringResponseParser, string>(
          "docker".DockerPath(),
          arg).Execute();
    }

    public static string Stop(this Uri host, string id, TimeSpan? killTimeout = null, string caCertPath = null,
      string clientCertPath = null,
      string clientKeyPath = null)
    {
      var arg = $"{RenderBaseArgs(host, caCertPath, clientCertPath, clientKeyPath)} stop";
      if (null != killTimeout)
      {
        arg += $" --time={Math.Round(killTimeout.Value.TotalSeconds, 0)}";
      }

      arg += $" {id}";

      return new ProcessExecutor<SingleStringResponseParser, string>(
        "docker".DockerPath(),
        arg).Execute();
    }

    public static string RemoveContainer(this Uri host, string id, bool force = false, bool removeVolumes = false,
      string removeLink = null, string caCertPath = null,
      string clientCertPath = null,
      string clientKeyPath = null)
    {
      var arg = $"{RenderBaseArgs(host, caCertPath, clientCertPath, clientKeyPath)} rm";
      if (force)
      {
        arg += " --force";
      }

      if (removeVolumes)
      {
        arg += " --volumes";
      }

      if (!string.IsNullOrEmpty(removeLink))
      {
        arg += $" --link {removeLink}";
      }

      arg += $" {id}";

      return new ProcessExecutor<SingleStringResponseParser, string>(
        "docker".DockerPath(),
        arg).Execute();
    }

    public static Container InspectContainer(this Uri host, string id, string caCertPath = null,
      string clientCertPath = null,
      string clientKeyPath = null)
    {
      return new ProcessExecutor<ClientContainerInspectCommandResponder, Container>("docker".DockerPath(),
        $"{RenderBaseArgs(host, caCertPath, clientCertPath, clientKeyPath)} inspect {id}").Execute();
    }

    private static string RenderBaseArgs(Uri host, string caCertPath = null, string clientCertPath = null,
      string clientKeyPath = null)
    {
      return null != caCertPath
        ? $" -H {host.Host}:{host.Port} --tlsverify=true --tlscacert={caCertPath} --tlscert={clientCertPath} --tlskey={clientKeyPath}"
        : string.Empty;
    }
  }
}