using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Tests.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.FluentApiTests
{
  [TestClass]
  public sealed class FluentVolumeTests
  {
    private static DockerUri _docker;
    private static CertificatePaths _certificates;
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
        "test-machine".Delete(true /*force*/);
    }

    [TestMethod]
    public void VolumeShallNotDeletedWhenRemoveOnDisposeIsNotPresent()
    {
      string id = null;
      try
      {
        using (var vol = Fd.UseVolume("test-volume").Build())
        {
          vol.GetConfiguration(true); // Will throw FluentDockerException if fails
          id = vol.Name;
        }

        var v = _docker.VolumeInspect(_certificates, id);
        Assert.IsTrue(v.Success);
        Assert.AreEqual(1, v.Data.Count);
        Assert.AreEqual(id, v.Data[0].Name);
      }
      finally
      {
        if (null != id)
          _docker.VolumeRm(_certificates, force: true, volume: id);
      }
    }

    [TestMethod]
    public void VolumeShallBeDeletedWhenRemoveOnDispose()
    {
      string id;
      using (var vol = Fd.UseVolume("test-volume").RemoveOnDispose().Build())
      {
        vol.GetConfiguration(true); // Will throw FluentDockerException if fails
        id = vol.Name;
      }

      var v = _docker.VolumeInspect(_certificates, id);
      Assert.IsFalse(v.Success);
    }

    [TestMethod]
    public void VolumeShallBeUsedWhenMounted()
    {
      using (var vol = Fd.UseVolume("test-volume").RemoveOnDispose().Build())
      {
        using (
          var container =
            Fd.UseContainer()
              .UseImage("postgres:9.6-alpine")
              .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
              .MountVolume(vol, "/var/lib/postgresql/data", MountType.ReadWrite)
              .Build()
              .Start())
        {
          var config = container.GetConfiguration();

          Assert.AreEqual(1, config.Mounts.Length);
          Assert.AreEqual("test-volume", config.Mounts[0].Name);
        }
      }
    }
  }
}
