﻿using System.Diagnostics;
using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services.Extensions;
using Ductus.FluentDocker.Tests.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

// ReSharper disable StringLiteralTypo

namespace Ductus.FluentDocker.Tests.CommandTests
{
  [TestClass]
  public class DockerClientCommandTests
  {
    private static CertificatePaths _certificates;
    private static DockerUri _docker;
    private static bool _createdTestMachine;

    [ClassInitialize]
    public static void Initialize(TestContext ctx)
    {
      if (CommandExtensions.IsNative() || CommandExtensions.IsEmulatedNative())
      {
        _docker.LinuxMode();
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

    [TestMethod]
    public void EnsureLinuxDaemonShallWork()
    {
      _docker.LinuxDaemon(_certificates);
      var mode = _docker.Version(_certificates);
      AreEqual("linux", mode.Data.ServerOs);
    }

    [TestMethod]
    [Ignore]
    public void EnsureWindowsDaemonShallWork()
    {
      if (!FdOs.IsWindows())
      {
        // Only run this test on windows devices.
        return;
      }

      try
      {
        _docker.WindowsDaemon(_certificates);
        var mode = _docker.Version(_certificates);
        AreEqual("windows", mode.Data.ServerOs);
      }
      finally
      {
        _docker.LinuxDaemon(_certificates);
      }

    }

    [TestMethod]
    public void RunWithoutArgumentShallSucceed()
    {
      string id = null;
      try
      {
        var cmd = _docker.Run("nginx:1.13.6-alpine", null, _certificates);
        IsTrue(cmd.Success);

        id = cmd.Data;
        IsTrue(!string.IsNullOrWhiteSpace(id));
        AreEqual(64, id.Length);
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
    public void RemoveContainerShallSucceed()
    {
      string id = null;
      try
      {
        var cmd = _docker.Run("nginx:1.13.6-alpine", null, _certificates);
        IsTrue(cmd.Success);

        id = cmd.Data;
        IsTrue(!string.IsNullOrWhiteSpace(id));
        AreEqual(64, id.Length);

        var rm = _docker.RemoveContainer(id, true, true, null, _certificates);
        IsTrue(rm.Success);
        AreEqual(id, rm.Data);
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
    public void DockerPsWithOneContainerShallGiveOneResult()
    {
      string id = null;
      try
      {
        var cmd = _docker.Run("nginx:1.13.6-alpine", null, _certificates);
        IsTrue(cmd.Success);

        id = cmd.Data;
        IsTrue(!string.IsNullOrWhiteSpace(id));
        AreEqual(64, id.Length);

        var ls = _docker.Ps(null, _certificates);
        IsTrue(ls.Success);
        AreEqual(1, ls.Data.Count);
        IsTrue(id.StartsWith(ls.Data[0]));
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
    public void InspectRunningContainerShallSucceed()
    {
      string id = null;
      try
      {
        var cmd = _docker.Run("nginx:1.13.6-alpine", null, _certificates);
        IsTrue(cmd.Success);

        id = cmd.Data;
        IsTrue(!string.IsNullOrWhiteSpace(id));
        AreEqual(64, id.Length);

        var inspect = _docker.InspectContainer(id, _certificates);
        IsTrue(inspect.Success);
        IsTrue(inspect.Data.Name.Length > 2);
        AreEqual(true, inspect.Data.State.Running);
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
    public void InspectContainersShallSucceed()
    {
      // Arrange

      const string image1 = "nginx:1.13.6-alpine";
      const string image2 = "postgres:9.6-alpine";
      string id1 = null;
      string id2 = null;

      try
      {
        var cmd = _docker.Run(image1, null, _certificates);
        IsTrue(cmd.Success);
        id1 = cmd.Data;
        IsTrue(!string.IsNullOrWhiteSpace(id1));
        AreEqual(64, id1.Length);

        cmd = _docker.Create(image2, null);
        IsTrue(cmd.Success);
        id2 = cmd.Data;
        IsTrue(!string.IsNullOrWhiteSpace(id2));
        AreEqual(64, id2.Length);


        // Act
        var result = _docker.InspectContainers(_certificates);


        // Assert
        IsTrue(result.Success);
        IsTrue(result.Data.Count > 1);
        IsNotNull(result.Data.SingleOrDefault(c => c.Id == id1));
        IsNotNull(result.Data.SingleOrDefault(c => c.Id == id2));
      }
      finally
      {
        if (null != id1)
        {
          _docker.RemoveContainer(id1, true, true, null, _certificates);
        }
        if (null != id2)
        {
          _docker.RemoveContainer(id2, true, true, null, _certificates);
        }
      }
    }

    [TestMethod]
    public void InspectContainersByIdsShallSucceed()
    {
      // Arrange

      const string image1 = "nginx:1.13.6-alpine";
      const string image2 = "postgres:9.6-alpine";
      string id1 = null;
      string id2 = null;

      try
      {
        var cmd = _docker.Run(image1, null, _certificates);
        IsTrue(cmd.Success);
        id1 = cmd.Data;
        IsTrue(!string.IsNullOrWhiteSpace(id1));
        AreEqual(64, id1.Length);

        cmd = _docker.Create(image2, null);
        IsTrue(cmd.Success);
        id2 = cmd.Data;
        IsTrue(!string.IsNullOrWhiteSpace(id2));
        AreEqual(64, id2.Length);


        // Act
        var result = _docker.InspectContainers(_certificates, id1, id2);


        // Assert
        IsTrue(result.Success);
        IsTrue(result.Data[0].Name.Length > 2);
        IsTrue(result.Data[0].State.Running);
        AreEqual(image1, result.Data[0].Config.Image);
        IsTrue(result.Data[1].Name.Length > 2);
        IsFalse(result.Data[1].State.Running);
        AreEqual(image2, result.Data[1].Config.Image);
      }
      finally
      {
        if (null != id1)
        {
          _docker.RemoveContainer(id1, true, true, null, _certificates);
        }
        if (null != id2)
        {
          _docker.RemoveContainer(id2, true, true, null, _certificates);
        }
      }
    }

    [TestMethod]
    public void DiffContainerShallWork()
    {
      string id = null;
      try
      {
        var cmd = _docker.Run("nginx:1.13.6-alpine", null, _certificates);
        IsTrue(cmd.Success);

        id = cmd.Data;
        IsTrue(!string.IsNullOrWhiteSpace(id));
        AreEqual(64, id.Length);

        var diffs = _docker.Diff(id, _certificates);
        IsTrue(diffs.Success);
        IsTrue(diffs.Data.Any(x => x.Type == DiffType.Created && x.Item == "/var/cache/nginx"));
        IsTrue(diffs.Data.Any(x => x.Type == DiffType.Added && x.Item == "/var/cache/nginx/client_temp"));
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
    public void RunPostgresContainerAndCheckThatAllProcessesAreRunning()
    {
      string id = null;
      try
      {
        var cmd = _docker.Run("postgres:9.6-alpine", new ContainerCreateParams
        {
          PublishAllPorts = true,
          Environment = new[] { "POSTGRES_PASSWORD=mysecretpassword" }
        }, _certificates);

        IsTrue(cmd.Success);

        id = cmd.Data;
        IsTrue(!string.IsNullOrWhiteSpace(id));
        AreEqual(64, id.Length);

        var config = _docker.InspectContainer(id, _certificates);
        IsTrue(config.Success);
        AreEqual(true, config.Data.State.Running);

        var endpoint = config.Data.NetworkSettings.Ports.ToHostPort("5432/tcp", _docker);
        endpoint.WaitForPort(10000 /*10s*/);

        var ls = _docker.Top(id, _certificates);
        IsTrue(ls.Success);

        var proc = ls.Data;
        Debug.WriteLine(proc.ToString());

        IsTrue(proc.Rows.Any(x => x.Command == "bash /usr/local/bin/docker-entrypoint.sh postgres"));
        IsTrue(proc.Rows.Any(x => x.Command == "pg_ctl -D /var/lib/postgresql/data -m fast -w stop"));
      }
      finally
      {
        if (null != id)
        {
          _docker.RemoveContainer(id, true, true, null, _certificates);
        }
      }
    }
  }
}
