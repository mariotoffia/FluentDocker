using System.Diagnostics;
using System.Linq;
using System.Threading;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Events;
using Ductus.FluentDocker.Services.Extensions;
using Ductus.FluentDocker.Tests.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.CommandTests
{
  [TestClass]
  public class ClientStreamCommandTests
  {
    private static CertificatePaths _certificates;
    private static DockerUri _docker;
    private static bool _createdTestMachine;

    [TestMethod]
    public void StartEventShallBeEmittedWhenContainerStart()
    {
      var host = Fd.Native();
      string id = null;

      try
      {
        using (var events = host.Events())
        {
          var cmd = _docker.Run("postgres:9.6-alpine", new ContainerCreateParams
          {
            PortMappings = new[] { "40001:5432" },
            Environment = new[] { "POSTGRES_PASSWORD=mysecretpassword" }
          }, _certificates);

          id = cmd.Data;

          FdEvent e = null;
          while ((e = events.TryRead(3000)) != null)
          {
            if (e.Type == EventType.Container && e.Action == EventAction.Start)
              break;
          }

          Assert.IsNotNull(e);
        }
      }
      finally
      {
        if (null != id)
        {
          _docker.RemoveContainer(id, true, true, null, _certificates);
        }
      }
    }

    [TestMethod]
    public void LogsFromContaierWhenNotFollowModeShallExitByItself()
    {
      string id = null;
      try
      {
        var cmd = _docker.Run("postgres:9.6-alpine", new ContainerCreateParams
        {
          PortMappings = new[] { "40001:5432" },
          Environment = new[] { "POSTGRES_PASSWORD=mysecretpassword" }
        }, _certificates);

        id = cmd.Data;
        var config = _docker.InspectContainer(id, _certificates);
        var endpoint = config.Data.NetworkSettings.Ports.ToHostPort("5432/tcp", _docker);
        endpoint.WaitForPort(10000 /*10s*/);

        using (var logs = _docker.Logs(id))
        {
          while (!logs.IsFinished)
          {
            var line = logs.TryRead(5000);
            if (null == line)
            {
              Assert.AreEqual(true, logs.IsFinished, "Since null line, the process shall been shutdown");
              break;
            }

            Debug.WriteLine(line);
          }

          Assert.AreEqual(true, logs.IsFinished);
          Assert.AreEqual(true, logs.IsSuccess);
        }
      }
      finally
      {
        if (null != id)
        {
          _docker.RemoveContainer(id, true, true, null, _certificates);
        }
      }
    }

    [TestMethod]
    public void LogsFromContaierWhenInFollowModeShallExitWhenCancelled()
    {
      string id = null;
      try
      {
        var cmd = _docker.Run("postgres:9.6-alpine", new ContainerCreateParams
        {
          PortMappings = new[] { "40001:5432" },
          Environment = new[] { "POSTGRES_PASSWORD=mysecretpassword" }
        }, _certificates);

        id = cmd.Data;
        var config = _docker.InspectContainer(id, _certificates);
        var endpoint = config.Data.NetworkSettings.Ports.ToHostPort("5432/tcp", _docker);
        endpoint.WaitForPort(10000 /*10s*/);

        var token = new CancellationTokenSource();
        using (var logs = _docker.Logs(id, token.Token, true/*follow*/))
        {
          while (!logs.IsFinished)
          {
            var line = logs.TryRead(5000);
            if (null == line)
            {
              Assert.AreEqual(false, logs.IsFinished);
              token.Cancel();
              Thread.Sleep(1000);
              break;
            }

            Debug.WriteLine(line);
          }

          Assert.AreEqual(true, logs.IsFinished);
          Assert.AreEqual(false, logs.IsSuccess);
        }
      }
      finally
      {
        if (null != id)
        {
          _docker.RemoveContainer(id, true, true, null, _certificates);
        }
      }
    }

    [TestMethod]
    public void LogFromContainerShouldSupportReadAllExtension()
    {
      string id = null;
      try
      {
        var cmd = _docker.Run("postgres:9.6-alpine", new ContainerCreateParams
        {
          PortMappings = new[] { "40001:5432" },
          Environment = new[] { "POSTGRES_PASSWORD=mysecretpassword" }
        }, _certificates);

        id = cmd.Data;
        var config = _docker.InspectContainer(id, _certificates);
        var endpoint = config.Data.NetworkSettings.Ports.ToHostPort("5432/tcp", _docker);
        endpoint.WaitForPort(10000 /*10s*/);

        using (var logs = _docker.Logs(id))
        {
          foreach (var line in logs.ReadToEnd())
          {
            Debug.WriteLine(line);
          }

          Assert.AreEqual(true, logs.IsFinished);
          Assert.AreEqual(true, logs.IsSuccess);
        }
      }
      finally
      {
        if (null != id)
        {
          _docker.RemoveContainer(id, true, true, null, _certificates);
        }
      }
    }

    [ClassInitialize]
    public static void Initialize(TestContext ctx)
    {
      if (CommandExtensions.IsNative() || CommandExtensions.IsEmulatedNative())
      {
        _docker.LinuxMode(_certificates);
        return;
      }

      var machineName = "test-machine";

      var machines = Machine.Ls();
      if (machines.Success && machines.Data.Any(x => x.Name == "default"))
      {
        machineName = "default";
      }
      else
      {
        machineName.Create(1024, 20000, 1);
        _createdTestMachine = true;
      }

      machineName.Start();
      var inspect = machineName.Inspect().Data;

      _docker = machineName.Uri();
      _certificates = new CertificatePaths
      {
        CaCertificate = inspect.AuthConfig.CaCertPath,
        ClientCertificate = inspect.AuthConfig.ClientCertPath,
        ClientKey = inspect.AuthConfig.ClientKeyPath
      };
      _docker.LinuxMode(_certificates);
    }

    [ClassCleanup]
    public static void TearDown()
    {
      if (_createdTestMachine)
      {
        "test-machine".Delete(true /*force*/);
      }
    }
  }
}
