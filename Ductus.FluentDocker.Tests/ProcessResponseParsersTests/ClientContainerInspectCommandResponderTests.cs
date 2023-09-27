using System;
using System.Reflection;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Executors.Parsers;
using Ductus.FluentDocker.Model.Containers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.ProcessResponseParsersTests
{
  [TestClass]
  public class ClientContainerInspectCommandResponderTests
  {
    [TestMethod]
    public void ProcessShallParseResponse()
    {
      // Arrange
      var stdOut = @"[
      {
          ""Id"": ""82b3c01497e365efa2330505d24a08daf67a5c554715fafe0eb6b1f0d34b8cd2"",
          ""Created"": ""2023-09-27T19:49:02.074054924Z"",
          ""Path"": ""docker-entrypoint.sh"",
          ""Args"": [
              ""postgres""
          ],
          ""State"": {
              ""Status"": ""running"",
              ""Running"": true,
              ""Paused"": false,
              ""Restarting"": false,
              ""OOMKilled"": false,
              ""Dead"": false,
              ""Pid"": 30202,
              ""ExitCode"": 0,
              ""Error"": """",
              ""StartedAt"": ""2023-09-27T19:49:02.247208341Z"",
              ""FinishedAt"": ""0001-01-01T00:00:00Z""
          },
          ""Image"": ""sha256:fbee27eada86c3e82d62d1a41d2258137cf7004b81b28c696943f20462dc3b0f"",
          ""ResolvConfPath"": ""/var/lib/docker/containers/82b3c01497e365efa2330505d24a08daf67a5c554715fafe0eb6b1f0d34b8cd2/resolv.conf"",
          ""HostnamePath"": ""/var/lib/docker/containers/82b3c01497e365efa2330505d24a08daf67a5c554715fafe0eb6b1f0d34b8cd2/hostname"",
          ""HostsPath"": ""/var/lib/docker/containers/82b3c01497e365efa2330505d24a08daf67a5c554715fafe0eb6b1f0d34b8cd2/hosts"",
          ""LogPath"": ""/var/lib/docker/containers/82b3c01497e365efa2330505d24a08daf67a5c554715fafe0eb6b1f0d34b8cd2/82b3c01497e365efa2330505d24a08daf67a5c554715fafe0eb6b1f0d34b8cd2-json.log"",
          ""Name"": ""/test-postgres"",
          ""RestartCount"": 0,
          ""Driver"": ""overlay2"",
          ""Platform"": ""linux"",
          ""MountLabel"": """",
          ""ProcessLabel"": """",
          ""AppArmorProfile"": """",
          ""ExecIDs"": null,
          ""HostConfig"": {
              ""Binds"": null,
              ""ContainerIDFile"": """",
              ""LogConfig"": {
                  ""Type"": ""json-file"",
                  ""Config"": {}
              },
              ""NetworkMode"": ""default"",
              ""PortBindings"": {},
              ""RestartPolicy"": {
                  ""Name"": ""no"",
                  ""MaximumRetryCount"": 0
              },
              ""AutoRemove"": false,
              ""VolumeDriver"": """",
              ""VolumesFrom"": null,
              ""ConsoleSize"": [
                  25,
                  214
              ],
              ""CapAdd"": null,
              ""CapDrop"": null,
              ""CgroupnsMode"": ""private"",
              ""Dns"": [],
              ""DnsOptions"": [],
              ""DnsSearch"": [],
              ""ExtraHosts"": null,
              ""GroupAdd"": null,
              ""IpcMode"": ""private"",
              ""Cgroup"": """",
              ""Links"": null,
              ""OomScoreAdj"": 0,
              ""PidMode"": """",
              ""Privileged"": false,
              ""PublishAllPorts"": false,
              ""ReadonlyRootfs"": false,
              ""SecurityOpt"": null,
              ""UTSMode"": """",
              ""UsernsMode"": """",
              ""ShmSize"": 67108864,
              ""Runtime"": ""runc"",
              ""Isolation"": """",
              ""CpuShares"": 0,
              ""Memory"": 0,
              ""NanoCpus"": 0,
              ""CgroupParent"": """",
              ""BlkioWeight"": 0,
              ""BlkioWeightDevice"": [],
              ""BlkioDeviceReadBps"": [],
              ""BlkioDeviceWriteBps"": [],
              ""BlkioDeviceReadIOps"": [],
              ""BlkioDeviceWriteIOps"": [],
              ""CpuPeriod"": 0,
              ""CpuQuota"": 0,
              ""CpuRealtimePeriod"": 0,
              ""CpuRealtimeRuntime"": 0,
              ""CpusetCpus"": """",
              ""CpusetMems"": """",
              ""Devices"": [],
              ""DeviceCgroupRules"": null,
              ""DeviceRequests"": null,
              ""MemoryReservation"": 0,
              ""MemorySwap"": 0,
              ""MemorySwappiness"": null,
              ""OomKillDisable"": null,
              ""PidsLimit"": null,
              ""Ulimits"": null,
              ""CpuCount"": 0,
              ""CpuPercent"": 0,
              ""IOMaximumIOps"": 0,
              ""IOMaximumBandwidth"": 0,
              ""MaskedPaths"": [
                  ""/proc/asound"",
                  ""/proc/acpi"",
                  ""/proc/kcore"",
                  ""/proc/keys"",
                  ""/proc/latency_stats"",
                  ""/proc/timer_list"",
                  ""/proc/timer_stats"",
                  ""/proc/sched_debug"",
                  ""/proc/scsi"",
                  ""/sys/firmware""
              ],
              ""ReadonlyPaths"": [
                  ""/proc/bus"",
                  ""/proc/fs"",
                  ""/proc/irq"",
                  ""/proc/sys"",
                  ""/proc/sysrq-trigger""
              ]
          },
          ""GraphDriver"": {
              ""Data"": {
                  ""LowerDir"": ""/var/lib/docker/overlay2/77bc9003377919ff798e09db0604c618901982b10d209d8226ddfcfa8cdb7650-init/diff:/var/lib/docker/overlay2/eb8f4366ce3fa95e9b6ca6232c68404f6207701a31d093ea9a650b46e1fa8063/diff:/var/lib/docker/overlay2/42edcd520759b2febd882900f711fd94e7361f5062f1ccdfe3234ab2d7fc8779/diff:/var/lib/docker/overlay2/d47154447653c8be2011e7dcba54d6dfa9c0358ec7570b6e9ebb44b6264ce06f/diff:/var/lib/docker/overlay2/08ddb175de10618255c6fff2e87230e3fd9991f44a789c82a31ab3364ccb9bc9/diff:/var/lib/docker/overlay2/b08490a4229abee1bf91d56dfffaa5cbdfac5b89a6c16938bbe8ec491564ecdc/diff:/var/lib/docker/overlay2/fef3614daa405946d03a906656193a47f9774ab860180f4a7067bdfca0482997/diff:/var/lib/docker/overlay2/e21f8bc0da91d55f6c486953b78dc59c9ca6ef1b3f2a5ab8d3934b77c70327e7/diff:/var/lib/docker/overlay2/ea89c59efada74850454eebc885945d3d5d0d5cb37a88353b3ecb56cec51653b/diff:/var/lib/docker/overlay2/82027facf58875506c0c31eef83e43cab5cd63f29411079441803c49c63cf153/diff:/var/lib/docker/overlay2/f8092acd49404ee192ca4cfa94d7498d98c465d3757b4dbfad6b581250a078fe/diff:/var/lib/docker/overlay2/17276a89947fc0c6a8c9c5acb62c1bae9cae60292458f7cb90c030c1d51919fa/diff:/var/lib/docker/overlay2/8b7c9d84bd1dbff3ef2bb3017e1e86f872b34f03f3278cfa737d21b6ab73ddab/diff:/var/lib/docker/overlay2/d6d180a24a2841fa104adacc63edc5718b977167cf35ee2be17cb796f300f270/diff"",
                  ""MergedDir"": ""/var/lib/docker/overlay2/77bc9003377919ff798e09db0604c618901982b10d209d8226ddfcfa8cdb7650/merged"",
                  ""UpperDir"": ""/var/lib/docker/overlay2/77bc9003377919ff798e09db0604c618901982b10d209d8226ddfcfa8cdb7650/diff"",
                  ""WorkDir"": ""/var/lib/docker/overlay2/77bc9003377919ff798e09db0604c618901982b10d209d8226ddfcfa8cdb7650/work""
              },
              ""Name"": ""overlay2""
          },
          ""Mounts"": [
              {
                  ""Type"": ""volume"",
                  ""Name"": ""03a7b3ffa92ff257d68cc458f2c0fd52061c37ca8ecaf9234ce33dfd58022c0f"",
                  ""Source"": ""/var/lib/docker/volumes/03a7b3ffa92ff257d68cc458f2c0fd52061c37ca8ecaf9234ce33dfd58022c0f/_data"",
                  ""Destination"": ""/var/lib/postgresql/data"",
                  ""Driver"": ""local"",
                  ""Mode"": """",
                  ""RW"": true,
                  ""Propagation"": """"
              }
          ],
          ""Config"": {
              ""Hostname"": ""82b3c01497e3"",
              ""Domainname"": """",
              ""User"": """",
              ""AttachStdin"": false,
              ""AttachStdout"": false,
              ""AttachStderr"": false,
              ""ExposedPorts"": {
                  ""5432/tcp"": {}
              },
              ""Tty"": false,
              ""OpenStdin"": false,
              ""StdinOnce"": false,
              ""Env"": [
                  ""POSTGRES_PASSWORD=password"",
                  ""PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/usr/lib/postgresql/16/bin"",
                  ""GOSU_VERSION=1.16"",
                  ""LANG=en_US.utf8"",
                  ""PG_MAJOR=16"",
                  ""PG_VERSION=16.0-1.pgdg120+1"",
                  ""PGDATA=/var/lib/postgresql/data""
              ],
              ""Cmd"": [
                  ""postgres""
              ],
              ""Image"": ""postgres"",
              ""Volumes"": {
                  ""/var/lib/postgresql/data"": {}
              },
              ""WorkingDir"": """",
              ""Entrypoint"": [
                  ""docker-entrypoint.sh""
              ],
              ""OnBuild"": null,
              ""Labels"": {},
              ""StopSignal"": ""SIGINT""
          },
          ""NetworkSettings"": {
              ""Bridge"": """",
              ""SandboxID"": ""d8e0b3a54e3b4c6cd059615be734fdff1f7ec9e65319593e321dc13d406b59ad"",
              ""HairpinMode"": false,
              ""LinkLocalIPv6Address"": """",
              ""LinkLocalIPv6PrefixLen"": 0,
              ""Ports"": {
                  ""5432/tcp"": null
              },
              ""SandboxKey"": ""/var/run/docker/netns/d8e0b3a54e3b"",
              ""SecondaryIPAddresses"": null,
              ""SecondaryIPv6Addresses"": null,
              ""EndpointID"": ""2135c241ecb5ad1b7b5c68d05b9e2f66fcc96eefdb04614d3cdf6964705a5d18"",
              ""Gateway"": ""172.17.0.1"",
              ""GlobalIPv6Address"": """",
              ""GlobalIPv6PrefixLen"": 0,
              ""IPAddress"": ""172.17.0.2"",
              ""IPPrefixLen"": 16,
              ""IPv6Gateway"": """",
              ""MacAddress"": ""02:42:ac:11:00:02"",
              ""Networks"": {
                  ""bridge"": {
                      ""IPAMConfig"": null,
                      ""Links"": null,
                      ""Aliases"": null,
                      ""NetworkID"": ""d55284e2feee89035ebfad8ee39f3921ee958c7074bc57a263aab435eab5f0b9"",
                      ""EndpointID"": ""2135c241ecb5ad1b7b5c68d05b9e2f66fcc96eefdb04614d3cdf6964705a5d18"",
                      ""Gateway"": ""172.17.0.1"",
                      ""IPAddress"": ""172.17.0.2"",
                      ""IPPrefixLen"": 16,
                      ""IPv6Gateway"": """",
                      ""GlobalIPv6Address"": """",
                      ""GlobalIPv6PrefixLen"": 0,
                      ""MacAddress"": ""02:42:ac:11:00:02"",
                      ""DriverOpts"": null
                  }
              }
          }
      }
  ]
  ";
      var ctorArgs = new object[] { "command", stdOut, "", 0 };
      var executionResult = (ProcessExecutionResult)Activator.CreateInstance(typeof(ProcessExecutionResult),
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.CreateInstance,
        null, ctorArgs, null, null);

      var parser = new ClientContainerInspectCommandResponder();

      // Act
      var result = parser.Process(executionResult);

      // Assert
      var container = result.Response.Data;
      Assert.AreEqual("82b3c01497e365efa2330505d24a08daf67a5c554715fafe0eb6b1f0d34b8cd2", container.Id);
      Assert.AreEqual("sha256:fbee27eada86c3e82d62d1a41d2258137cf7004b81b28c696943f20462dc3b0f", container.Image);
      Assert.AreEqual(new DateTime(2023, 09, 27, 19, 49, 02, DateTimeKind.Utc).AddTicks(740549), container.Created);
      Assert.AreEqual("/var/lib/docker/containers/82b3c01497e365efa2330505d24a08daf67a5c554715fafe0eb6b1f0d34b8cd2/resolv.conf", container.ResolvConfPath);
      Assert.AreEqual("/var/lib/docker/containers/82b3c01497e365efa2330505d24a08daf67a5c554715fafe0eb6b1f0d34b8cd2/hostname", container.HostnamePath);
      Assert.AreEqual("/var/lib/docker/containers/82b3c01497e365efa2330505d24a08daf67a5c554715fafe0eb6b1f0d34b8cd2/hosts", container.HostsPath);
      Assert.AreEqual("/var/lib/docker/containers/82b3c01497e365efa2330505d24a08daf67a5c554715fafe0eb6b1f0d34b8cd2/82b3c01497e365efa2330505d24a08daf67a5c554715fafe0eb6b1f0d34b8cd2-json.log", container.LogPath);
      Assert.AreEqual("test-postgres", container.Name);
      Assert.AreEqual(0, container.RestartCount);
      Assert.AreEqual("overlay2", container.Driver);

      Assert.AreEqual(1, container.Args.Length);
      Assert.AreEqual("postgres", container.Args[0]);

      Assert.AreEqual("running", container.State.Status);
      Assert.AreEqual(true, container.State.Running);
      Assert.AreEqual(false, container.State.Paused);
      Assert.AreEqual(false, container.State.Restarting);
      Assert.AreEqual(false, container.State.OOMKilled);
      Assert.AreEqual(false, container.State.Dead);
      Assert.AreEqual(30202, container.State.Pid);
      Assert.AreEqual(0, container.State.ExitCode);
      Assert.AreEqual("", container.State.Error);
      Assert.AreEqual(new DateTime(2023, 09, 27, 19, 49, 02, DateTimeKind.Utc).AddTicks(2472083), container.State.StartedAt);
      Assert.AreEqual(new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Utc), container.State.FinishedAt);
      Assert.IsNull(container.State.Health);

      Assert.AreEqual(1, container.Mounts.Length);
      var mount = container.Mounts[0];
      Assert.AreEqual("03a7b3ffa92ff257d68cc458f2c0fd52061c37ca8ecaf9234ce33dfd58022c0f", mount.Name);
      Assert.AreEqual("/var/lib/docker/volumes/03a7b3ffa92ff257d68cc458f2c0fd52061c37ca8ecaf9234ce33dfd58022c0f/_data", mount.Source);
      Assert.AreEqual("/var/lib/postgresql/data", mount.Destination);
      Assert.AreEqual("local", mount.Driver);
      Assert.AreEqual("", mount.Mode);
      Assert.AreEqual(true, mount.RW);
      Assert.AreEqual("", mount.Propagation);

      Assert.AreEqual("82b3c01497e3", container.Config.Hostname);
      Assert.AreEqual("", container.Config.DomainName);
      Assert.AreEqual("", container.Config.User);
      Assert.AreEqual(false, container.Config.AttachStdin);
      Assert.AreEqual(false, container.Config.AttachStdout);
      Assert.AreEqual(false, container.Config.AttachStderr);
      Assert.AreEqual(1, container.Config.ExposedPorts.Count);
      Assert.IsTrue(container.Config.ExposedPorts.ContainsKey("5432/tcp"));
      Assert.AreEqual(false, container.Config.Tty);
      Assert.AreEqual(false, container.Config.OpenStdin);
      Assert.AreEqual(false, container.Config.StdinOnce);
      var containerEnv = container.Config.Env;
      Assert.AreEqual(7, containerEnv.Length);
      Assert.AreEqual("POSTGRES_PASSWORD=password", containerEnv[0]);
      Assert.AreEqual("PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/usr/lib/postgresql/16/bin", containerEnv[1]);
      Assert.AreEqual("GOSU_VERSION=1.16", containerEnv[2]);
      Assert.AreEqual("LANG=en_US.utf8", containerEnv[3]);
      Assert.AreEqual("PG_MAJOR=16", containerEnv[4]);
      Assert.AreEqual("PG_VERSION=16.0-1.pgdg120+1", containerEnv[5]);
      Assert.AreEqual("PGDATA=/var/lib/postgresql/data", containerEnv[6]);
      Assert.AreEqual(1, container.Config.Cmd.Length);
      Assert.AreEqual("postgres", container.Config.Cmd[0]);
      Assert.AreEqual("postgres", container.Config.Image);
      Assert.AreEqual(1, container.Config.Volumes.Count);
      Assert.IsTrue(container.Config.Volumes.ContainsKey("/var/lib/postgresql/data"));
      Assert.AreEqual("", container.Config.WorkingDir);
      Assert.AreEqual(1, container.Config.EntryPoint.Length);
      Assert.AreEqual("docker-entrypoint.sh", container.Config.EntryPoint[0]);
      Assert.AreEqual(0, container.Config.Labels.Count);
      Assert.AreEqual("SIGINT", container.Config.StopSignal);

      Assert.AreEqual("", container.NetworkSettings.Bridge);
      Assert.AreEqual("d8e0b3a54e3b4c6cd059615be734fdff1f7ec9e65319593e321dc13d406b59ad", container.NetworkSettings.SandboxID);
      Assert.AreEqual(false, container.NetworkSettings.HairpinMode);
      Assert.AreEqual("", container.NetworkSettings.LinkLocalIPv6Address);
      Assert.AreEqual("0", container.NetworkSettings.LinkLocalIPv6PrefixLen);
      Assert.AreEqual(1, container.NetworkSettings.Ports.Count);
      Assert.IsNull(container.NetworkSettings.Ports["5432/tcp"]);
      Assert.AreEqual("/var/run/docker/netns/d8e0b3a54e3b", container.NetworkSettings.SandboxKey);
      Assert.IsNull(container.NetworkSettings.SecondaryIPAddresses);
      Assert.IsNull(container.NetworkSettings.SecondaryIPv6Addresses);
      Assert.AreEqual("2135c241ecb5ad1b7b5c68d05b9e2f66fcc96eefdb04614d3cdf6964705a5d18", container.NetworkSettings.EndpointID);
      Assert.AreEqual("172.17.0.1", container.NetworkSettings.Gateway);
      Assert.AreEqual("", container.NetworkSettings.GlobalIPv6Address);
      Assert.AreEqual("0", container.NetworkSettings.GlobalIPv6PrefixLen);
      Assert.AreEqual("172.17.0.2", container.NetworkSettings.IPAddress);
      Assert.AreEqual("16", container.NetworkSettings.IPPrefixLen);
      Assert.AreEqual("", container.NetworkSettings.IPv6Gateway);
      Assert.AreEqual("02:42:ac:11:00:02", container.NetworkSettings.MacAddress);
      Assert.AreEqual(1, container.NetworkSettings.Networks.Count);
      var bridgeNetwork = container.NetworkSettings.Networks["bridge"];
      Assert.IsNull(bridgeNetwork.Aliases);
      Assert.AreEqual("d55284e2feee89035ebfad8ee39f3921ee958c7074bc57a263aab435eab5f0b9", bridgeNetwork.NetworkID);
      Assert.AreEqual("2135c241ecb5ad1b7b5c68d05b9e2f66fcc96eefdb04614d3cdf6964705a5d18", bridgeNetwork.EndpointID);
      Assert.AreEqual("172.17.0.1", bridgeNetwork.Gateway);
      Assert.AreEqual("172.17.0.2", bridgeNetwork.IPAddress);
      Assert.AreEqual(16, bridgeNetwork.IPPrefixLen);
      Assert.AreEqual("", bridgeNetwork.IPv6Gateway);
      Assert.AreEqual("", bridgeNetwork.GlobalIPv6Address);
      Assert.AreEqual(0, bridgeNetwork.GlobalIPv6PrefixLen);
      Assert.AreEqual("02:42:ac:11:00:02", bridgeNetwork.MacAddress);
    }

    /// <summary>
    /// Podman output should ideally be identical (or close as possible) to docker output, however, there are some
    /// edge cases that don't require wild differences in parsing for the purposes of FluentDocker. These are:
    ///   - State.Health.Status is present, but empty string ""
    ///   - Config.Entrypoint is single string value instead of single string value inside of array
    ///
    /// The below output, captured from podman, is a representative example of these issues
    /// </summary>
    [TestMethod]
    public void ProcessShallParsePodmanOutputResponse()
    {
      // Arrange
      var stdOut = @"[
       {
            ""Id"": ""f2b2805a9c8f54681f0b4035b730f4466182b6e4a96b32dfe41ad90eec18e0ff"",
            ""Created"": ""2023-09-26T07:08:11.781224111+01:00"",
            ""Path"": ""docker-entrypoint.sh"",
            ""Args"": [
                 ""postgres"",
                 ""-c"",
                 ""log_statement=all""
            ],
            ""State"": {
                 ""OciVersion"": ""1.1.0-rc.3"",
                 ""Status"": ""created"",
                 ""Running"": false,
                 ""Paused"": false,
                 ""Restarting"": false,
                 ""OOMKilled"": false,
                 ""Dead"": false,
                 ""Pid"": 0,
                 ""ExitCode"": 0,
                 ""Error"": """",
                 ""StartedAt"": ""0001-01-01T00:00:00Z"",
                 ""FinishedAt"": ""0001-01-01T00:00:00Z"",
                 ""Health"": {
                      ""Status"": """",
                      ""FailingStreak"": 0,
                      ""Log"": null
                 },
                 ""CheckpointedAt"": ""0001-01-01T00:00:00Z"",
                 ""RestoredAt"": ""0001-01-01T00:00:00Z""
            },
            ""Image"": ""83699f7b0d2c6ceef728c0276c97fa5e91f54132920183b1a3a3d4bfd572f6a8"",
            ""ImageDigest"": ""sha256:00e6ed9967881099ce9e552be567537d0bb47c990dacb43229cc9494bfddd8a0"",
            ""ImageName"": ""docker.io/library/postgres:11.16-alpine"",
            ""Rootfs"": """",
            ""Pod"": """",
            ""ResolvConfPath"": """",
            ""HostnamePath"": """",
            ""HostsPath"": """",
            ""StaticDir"": ""/var/lib/containers/storage/overlay-containers/f2b2805a9c8f54681f0b4035b730f4466182b6e4a96b32dfe41ad90eec18e0ff/userdata"",
            ""OCIRuntime"": ""crun"",
            ""ConmonPidFile"": ""/run/containers/storage/overlay-containers/f2b2805a9c8f54681f0b4035b730f4466182b6e4a96b32dfe41ad90eec18e0ff/userdata/conmon.pid"",
            ""PidFile"": ""/run/containers/storage/overlay-containers/f2b2805a9c8f54681f0b4035b730f4466182b6e4a96b32dfe41ad90eec18e0ff/userdata/pidfile"",
            ""Name"": ""test-postgres"",
            ""RestartCount"": 0,
            ""Driver"": ""overlay"",
            ""MountLabel"": ""system_u:object_r:container_file_t:s0:c391,c801"",
            ""ProcessLabel"": ""system_u:system_r:container_t:s0:c391,c801"",
            ""AppArmorProfile"": """",
            ""EffectiveCaps"": [
                 ""CAP_CHOWN"",
                 ""CAP_DAC_OVERRIDE"",
                 ""CAP_FOWNER"",
                 ""CAP_FSETID"",
                 ""CAP_KILL"",
                 ""CAP_NET_BIND_SERVICE"",
                 ""CAP_SETFCAP"",
                 ""CAP_SETGID"",
                 ""CAP_SETPCAP"",
                 ""CAP_SETUID"",
                 ""CAP_SYS_CHROOT""
            ],
            ""BoundingCaps"": [
                 ""CAP_CHOWN"",
                 ""CAP_DAC_OVERRIDE"",
                 ""CAP_FOWNER"",
                 ""CAP_FSETID"",
                 ""CAP_KILL"",
                 ""CAP_NET_BIND_SERVICE"",
                 ""CAP_SETFCAP"",
                 ""CAP_SETGID"",
                 ""CAP_SETPCAP"",
                 ""CAP_SETUID"",
                 ""CAP_SYS_CHROOT""
            ],
            ""ExecIDs"": [],
            ""GraphDriver"": {
                 ""Name"": ""overlay"",
                 ""Data"": {
                      ""LowerDir"": ""/var/lib/containers/storage/overlay/6bf9cd0e63b06eb11653e354cfc55444c686f8362a86db160ab4e4ae12db3e1d/diff:/var/lib/containers/storage/overlay/3acd83daeb2d1d27e89c0546d0e0cd482a435d851cb5b08704b6b07e5959b340/diff:/var/lib/containers/storage/overlay/85718c02503a4fc52100de3d19d187b357cc0d840f7f5af2ddd41486a09dd65d/diff:/var/lib/containers/storage/overlay/ec5fd00bf7d2462b93c53fa6023cd870fb8654159308c33254f4da92d3634d76/diff:/var/lib/containers/storage/overlay/79ef70484e120299629a54bf56f6dd77448d0e5931b4ee7535ea01bb6b6169b4/diff:/var/lib/containers/storage/overlay/b7f9200cdc18821e98a876ee3783bf657eddf58b6b85c941f064b3441305a2d0/diff:/var/lib/containers/storage/overlay/9bdbaa99d8fe24a83bc29c65adad6a6aadd2b3f6647ee476cc7770da63f9f611/diff:/var/lib/containers/storage/overlay/5d3e392a13a0fdfbf8806cb4a5e4b0a92b5021103a146249d8a2c999f06a9772/diff"",
                      ""UpperDir"": ""/var/lib/containers/storage/overlay/836bd449504a6b8a3b225e0bed18df35d13cbf02250f6095d09aa0b6bd4bc3e3/diff"",
                      ""WorkDir"": ""/var/lib/containers/storage/overlay/836bd449504a6b8a3b225e0bed18df35d13cbf02250f6095d09aa0b6bd4bc3e3/work""
                 }
            },
            ""Mounts"": [
                 {
                      ""Type"": ""volume"",
                      ""Name"": ""7c85b753cfa8603ebbad215e50e20a755f3a91f37de7633f9cded22aab63ef7c"",
                      ""Source"": ""/var/lib/containers/storage/volumes/7c85b753cfa8603ebbad215e50e20a755f3a91f37de7633f9cded22aab63ef7c/_data"",
                      ""Destination"": ""/var/lib/postgresql/data"",
                      ""Driver"": ""local"",
                      ""Mode"": """",
                      ""Options"": [
                           ""nodev"",
                           ""exec"",
                           ""nosuid"",
                           ""rbind""
                      ],
                      ""RW"": true,
                      ""Propagation"": ""rprivate""
                 }
            ],
            ""Dependencies"": [],
            ""NetworkSettings"": {
                 ""EndpointID"": """",
                 ""Gateway"": """",
                 ""IPAddress"": """",
                 ""IPPrefixLen"": 0,
                 ""IPv6Gateway"": """",
                 ""GlobalIPv6Address"": """",
                 ""GlobalIPv6PrefixLen"": 0,
                 ""MacAddress"": """",
                 ""Bridge"": """",
                 ""SandboxID"": """",
                 ""HairpinMode"": false,
                 ""LinkLocalIPv6Address"": """",
                 ""LinkLocalIPv6PrefixLen"": 0,
                 ""Ports"": {
                      ""5432/tcp"": [
                           {
                                ""HostIp"": """",
                                ""HostPort"": ""5432""
                           }
                      ]
                 },
                 ""SandboxKey"": """",
                 ""Networks"": {
                      ""podman"": {
                           ""EndpointID"": """",
                           ""Gateway"": """",
                           ""IPAddress"": """",
                           ""IPPrefixLen"": 0,
                           ""IPv6Gateway"": """",
                           ""GlobalIPv6Address"": """",
                           ""GlobalIPv6PrefixLen"": 0,
                           ""MacAddress"": """",
                           ""NetworkID"": ""podman"",
                           ""DriverOpts"": null,
                           ""IPAMConfig"": null,
                           ""Links"": null,
                           ""Aliases"": [
                                ""f2b2805a9c8f""
                           ]
                      }
                 }
            },
            ""Namespace"": """",
            ""IsInfra"": false,
            ""IsService"": false,
            ""KubeExitCodePropagation"": ""invalid"",
            ""lockNumber"": 1,
            ""Config"": {
                 ""Hostname"": ""f2b2805a9c8f"",
                 ""Domainname"": """",
                 ""User"": """",
                 ""AttachStdin"": false,
                 ""AttachStdout"": false,
                 ""AttachStderr"": false,
                 ""Tty"": false,
                 ""OpenStdin"": false,
                 ""StdinOnce"": false,
                 ""Env"": [
                      ""PGDATA=/var/lib/postgresql/data"",
                      ""LANG=en_US.utf8"",
                      ""POSTGRES_PASSWORD=password"",
                      ""PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"",
                      ""TERM=xterm"",
                      ""PG_MAJOR=11"",
                      ""POSTGRES_DB=userdb"",
                      ""POSTGRES_USER=user"",
                      ""container=podman"",
                      ""PG_VERSION=11.16"",
                      ""PG_SHA256=2dd9e111f0a5949ee7cacc065cea0fb21092929bae310ce05bf01b4ffc5103a5""
                 ],
                 ""Cmd"": [
                      ""postgres"",
                      ""-c"",
                      ""log_statement=all""
                 ],
                 ""Image"": ""docker.io/library/postgres:11.16-alpine"",
                 ""Volumes"": null,
                 ""WorkingDir"": ""/"",
                 ""Entrypoint"": ""docker-entrypoint.sh"",
                 ""OnBuild"": null,
                 ""Labels"": null,
                 ""Annotations"": null,
                 ""StopSignal"": 2,
                 ""HealthcheckOnFailureAction"": ""none"",
                 ""CreateCommand"": [
                      ""podman"",
                      ""create"",
                      ""--name"",
                      ""test-postgres"",
                      ""-p"",
                      ""5432:5432"",
                      ""-e"",
                      ""POSTGRES_PASSWORD=password"",
                      ""-e"",
                      ""POSTGRES_USER=user"",
                      ""-e"",
                      ""POSTGRES_DB=userdb"",
                      ""postgres:11.16-alpine"",
                      ""postgres"",
                      ""-c"",
                      ""log_statement=all""
                 ],
                 ""Umask"": ""0022"",
                 ""Timeout"": 0,
                 ""StopTimeout"": 10,
                 ""Passwd"": true,
                 ""sdNotifyMode"": ""container""
            },
            ""HostConfig"": {
                 ""Binds"": [
                      ""7c85b753cfa8603ebbad215e50e20a755f3a91f37de7633f9cded22aab63ef7c:/var/lib/postgresql/data:rprivate,rw,nodev,exec,nosuid,rbind""
                 ],
                 ""CgroupManager"": ""systemd"",
                 ""CgroupMode"": ""private"",
                 ""ContainerIDFile"": """",
                 ""LogConfig"": {
                      ""Type"": ""journald"",
                      ""Config"": null,
                      ""Path"": """",
                      ""Tag"": """",
                      ""Size"": ""0B""
                 },
                 ""NetworkMode"": ""bridge"",
                 ""PortBindings"": {
                      ""5432/tcp"": [
                           {
                                ""HostIp"": """",
                                ""HostPort"": ""5432""
                           }
                      ]
                 },
                 ""RestartPolicy"": {
                      ""Name"": """",
                      ""MaximumRetryCount"": 0
                 },
                 ""AutoRemove"": false,
                 ""VolumeDriver"": """",
                 ""VolumesFrom"": null,
                 ""CapAdd"": [],
                 ""CapDrop"": [],
                 ""Dns"": [],
                 ""DnsOptions"": [],
                 ""DnsSearch"": [],
                 ""ExtraHosts"": [],
                 ""GroupAdd"": [],
                 ""IpcMode"": ""shareable"",
                 ""Cgroup"": """",
                 ""Cgroups"": ""default"",
                 ""Links"": null,
                 ""OomScoreAdj"": 0,
                 ""PidMode"": ""private"",
                 ""Privileged"": false,
                 ""PublishAllPorts"": false,
                 ""ReadonlyRootfs"": false,
                 ""SecurityOpt"": [],
                 ""Tmpfs"": {},
                 ""UTSMode"": ""private"",
                 ""UsernsMode"": """",
                 ""ShmSize"": 65536000,
                 ""Runtime"": ""oci"",
                 ""ConsoleSize"": [
                      0,
                      0
                 ],
                 ""Isolation"": """",
                 ""CpuShares"": 0,
                 ""Memory"": 0,
                 ""NanoCpus"": 0,
                 ""CgroupParent"": """",
                 ""BlkioWeight"": 0,
                 ""BlkioWeightDevice"": null,
                 ""BlkioDeviceReadBps"": null,
                 ""BlkioDeviceWriteBps"": null,
                 ""BlkioDeviceReadIOps"": null,
                 ""BlkioDeviceWriteIOps"": null,
                 ""CpuPeriod"": 0,
                 ""CpuQuota"": 0,
                 ""CpuRealtimePeriod"": 0,
                 ""CpuRealtimeRuntime"": 0,
                 ""CpusetCpus"": """",
                 ""CpusetMems"": """",
                 ""Devices"": [],
                 ""DiskQuota"": 0,
                 ""KernelMemory"": 0,
                 ""MemoryReservation"": 0,
                 ""MemorySwap"": 0,
                 ""MemorySwappiness"": 0,
                 ""OomKillDisable"": false,
                 ""PidsLimit"": 2048,
                 ""Ulimits"": [
                      {
                           ""Name"": ""RLIMIT_NPROC"",
                           ""Soft"": 4194304,
                           ""Hard"": 4194304
                      }
                 ],
                 ""CpuCount"": 0,
                 ""CpuPercent"": 0,
                 ""IOMaximumIOps"": 0,
                 ""IOMaximumBandwidth"": 0,
                 ""CgroupConf"": null
            }
       }
  ]";
      var ctorArgs = new object[] { "command", stdOut, "", 0 };
      var executionResult = (ProcessExecutionResult)Activator.CreateInstance(typeof(ProcessExecutionResult),
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.CreateInstance,
        null, ctorArgs, null, null);

      var parser = new ClientContainerInspectCommandResponder();

      // Act
      var result = parser.Process(executionResult);

      // Assert
      Assert.IsNull(result.Response.Data.State.Health.Status);
      Assert.AreEqual(1, result.Response.Data.Config.EntryPoint.Length);
      Assert.AreEqual("docker-entrypoint.sh", result.Response.Data.Config.EntryPoint[0]);
    }
  }
}
