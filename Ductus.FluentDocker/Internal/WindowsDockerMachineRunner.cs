using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ductus.FluentDocker.Internal
{
  public sealed class WindowsDockerMachineRunner
  {
    /*
    C:\Program Files\Docker Toolbox>docker-machine.exe stop default
Stopping "default"...
Machine "default" was stopped.

C:\Program Files\Docker Toolbox>docker-machine.exe start default
Starting "default"...
(default) Check network to re-create if needed...
(default) Waiting for an IP...
Machine "default" was started.
Waiting for SSH to be available...
Detecting the provisioner...
Started machines may have new IP addresses. You may need to re-run the `docker-machine env` command.

C:\Program Files\Docker Toolbox>docker-machine.exe start default
Starting "default"...
Machine "default" is already running.

C:\Program Files\Docker Toolbox>docker-machine.exe env
SET DOCKER_TLS_VERIFY=1
SET DOCKER_HOST=tcp://192.168.99.100:2376
SET DOCKER_CERT_PATH=C:\Users\mario\.docker\machine\machines\default
SET DOCKER_MACHINE_NAME=default
REM Run this command to configure your shell:
REM     FOR /f "tokens=*" %i IN ('docker-machine.exe env') DO %i

C:\Program Files\Docker Toolbox>FOR /f "tokens=*" %i IN ('docker-machine.exe env') DO %i
*/
    public bool IsDockerToolbox => null != ToolBoxPath;

    public Process Process
    {
      get
      {
        var pr = Process.GetProcesses().Where(x => x.ProcessName == "bash" && x.MainWindowTitle.Contains("MINGW")).ToArray();
        foreach (var p in pr)
        {
          var pe = p.StartInfo.EnvironmentVariables;
        }

        return
          (from process in
            Process.GetProcesses().Where(x => x.ProcessName == "bash" && x.MainWindowTitle.Contains("MINGW"))
            let env = process.StartInfo.EnvironmentVariables
            where
              env.ContainsKey(DockerBuilder.DockerHost) && env.ContainsKey(DockerBuilder.DockerCertPath) &&
              env.ContainsKey(DockerBuilder.DockerMachineName)
            select process).FirstOrDefault();
      }
    }

    private string ToolBoxPath => Environment.GetEnvironmentVariable(DockerBuilder.DockerToolboxInstallPath);

    private string BashPath
    {
      get
      {
        const string drives = "CDEFGHIJKLMNOPQRSTUVWXYZ";
        foreach (var drive in drives)
        {
          var path = $"{drive}:\\Program Files\\Git\\bin\\bash.exe";
          if (File.Exists(path))
          {
            return path;
          }

          var path86 = $"{drive}:\\Program Files (x86)\\Git\\bin\\bash.exe";
          if (File.Exists(path86))
          {
            return path86;
          }
        }

        return null;
      }
    }

    public Task<bool> StartAsync(long millisTimeout)
    {
      var workingDir = ToolBoxPath;
      var args = $"--login -i \"{workingDir}\\start.sh\"";
      var program = BashPath;

      var task = Task.Factory.StartNew(() =>
      {
        var pi = new ProcessStartInfo(program, args) {WorkingDirectory = workingDir};
        var process = Process.Start(pi);
        if (null == process)
        {
          return false;
        }

        do
        {
          var p = Process;
          if (null != p)
          {
            return true;
          }

          Thread.Sleep(1000);
          millisTimeout -= 1000;

        } while (millisTimeout > 0);

        return false;
      });

      return task;
    }

    public bool IsRunning()
    {
      return null != Process;
    }
  }
}