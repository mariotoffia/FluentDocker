using System.Collections.Generic;
using System.Reflection;
using FluentDocker.Drivers.Podman;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Drivers.Podman.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
    [Trait("Category", "Unit")]
    public class PodmanCliMachineDriverTests
    {
        #region BuildInitArgs Tests

        [Fact]
        public void BuildInitArgs_MinimalConfig_ReturnsBasicCommand()
        {
            var config = new MachineInitConfig();
            var result = InvokeBuildInitArgs(config);
            Assert.Equal("machine init", result);
        }

        [Fact]
        public void BuildInitArgs_WithName_IncludesName()
        {
            var config = new MachineInitConfig { Name = "my-vm" };
            var result = InvokeBuildInitArgs(config);
            Assert.Equal("machine init my-vm", result);
        }

        [Fact]
        public void BuildInitArgs_FullConfig_IncludesAllFlags()
        {
            var config = new MachineInitConfig
            {
                Name = "test-vm",
                Cpus = 4,
                DiskSizeGiB = 50,
                MemoryMiB = 4096,
                Rootful = true,
                Image = "quay.io/podman/machine-os:5.0",
                Username = "testuser",
                Now = true,
                Volumes = new List<string> { "/host:/vm" }
            };

            var result = InvokeBuildInitArgs(config);

            Assert.Contains("--cpus 4", result);
            Assert.Contains("--disk-size 50", result);
            Assert.Contains("--memory 4096", result);
            Assert.Contains("--rootful", result);
            Assert.Contains("--image quay.io/podman/machine-os:5.0", result);
            Assert.Contains("--username testuser", result);
            Assert.Contains("--now", result);
            Assert.Contains("-v /host:/vm", result);
            Assert.EndsWith("test-vm", result);
            Assert.StartsWith("machine init", result);
        }

        [Fact]
        public void BuildInitArgs_NoRootful_OmitsFlag()
        {
            var config = new MachineInitConfig { Rootful = false };
            var result = InvokeBuildInitArgs(config);
            Assert.DoesNotContain("--rootful", result);
        }

        [Fact]
        public void BuildInitArgs_NoNow_OmitsFlag()
        {
            var config = new MachineInitConfig { Now = false };
            var result = InvokeBuildInitArgs(config);
            Assert.DoesNotContain("--now", result);
        }

        #endregion

        #region BuildSetArgs Tests

        [Fact]
        public void BuildSetArgs_EmptyConfig_ReturnsBasicCommand()
        {
            var config = new MachineSetConfig();
            var result = InvokeBuildSetArgs(config, null);
            Assert.Equal("machine set", result);
        }

        [Fact]
        public void BuildSetArgs_WithCpus_IncludesCpusFlag()
        {
            var config = new MachineSetConfig { Cpus = 8 };
            var result = InvokeBuildSetArgs(config, null);
            Assert.Contains("--cpus 8", result);
        }

        [Fact]
        public void BuildSetArgs_WithRootful_IncludesRootfulFlag()
        {
            var config = new MachineSetConfig { Rootful = true };
            var result = InvokeBuildSetArgs(config, null);
            Assert.Contains("--rootful", result);
        }

        [Fact]
        public void BuildSetArgs_WithRootfulFalse_IncludesRootfulEqualsFalse()
        {
            var config = new MachineSetConfig { Rootful = false };
            var result = InvokeBuildSetArgs(config, null);
            Assert.Contains("--rootful=false", result);
        }

        [Fact]
        public void BuildSetArgs_WithName_AppendsName()
        {
            var config = new MachineSetConfig { MemoryMiB = 2048 };
            var result = InvokeBuildSetArgs(config, "my-vm");
            Assert.Contains("--memory 2048", result);
            Assert.EndsWith("my-vm", result);
        }

        [Fact]
        public void BuildSetArgs_FullConfig_IncludesAllFlags()
        {
            var config = new MachineSetConfig
            {
                Cpus = 4,
                DiskSizeGiB = 100,
                MemoryMiB = 8192,
                Rootful = true
            };
            var result = InvokeBuildSetArgs(config, "test-vm");

            Assert.Contains("--cpus 4", result);
            Assert.Contains("--disk-size 100", result);
            Assert.Contains("--memory 8192", result);
            Assert.Contains("--rootful", result);
            Assert.EndsWith("test-vm", result);
        }

        #endregion

        #region ParseMachineList Tests

        [Fact]
        public void ParseMachineList_JsonArray_ReturnsMachines()
        {
            var json = @"[
                {
                    ""Name"": ""podman-machine-default"",
                    ""Default"": true,
                    ""Running"": true,
                    ""Created"": ""2026-01-15T10:30:00Z"",
                    ""LastUp"": ""2026-02-08T09:00:00Z"",
                    ""VMType"": ""applehv"",
                    ""CPUs"": 4,
                    ""Memory"": 4294967296,
                    ""DiskSize"": 107374182400
                },
                {
                    ""Name"": ""test-vm"",
                    ""Default"": false,
                    ""Running"": false,
                    ""VMType"": ""qemu"",
                    ""CPUs"": 2,
                    ""Memory"": 2147483648,
                    ""DiskSize"": 53687091200
                }
            ]";

            var result = InvokeParseMachineList(json);

            Assert.Equal(2, result.Count);
            Assert.Equal("podman-machine-default", result[0].Name);
            Assert.True(result[0].Default);
            Assert.True(result[0].Running);
            Assert.Equal("applehv", result[0].VMType);
            Assert.Equal(4, result[0].Cpus);
            Assert.Equal(4294967296L, result[0].Memory);
            Assert.Equal(107374182400L, result[0].DiskSize);

            Assert.Equal("test-vm", result[1].Name);
            Assert.False(result[1].Default);
            Assert.False(result[1].Running);
            Assert.Equal(2, result[1].Cpus);
        }

        [Fact]
        public void ParseMachineList_EmptyString_ReturnsEmpty()
        {
            Assert.Empty(InvokeParseMachineList(""));
            Assert.Empty(InvokeParseMachineList(null));
            Assert.Empty(InvokeParseMachineList("   "));
        }

        [Fact]
        public void ParseMachineList_EmptyArray_ReturnsEmpty()
        {
            Assert.Empty(InvokeParseMachineList("[]"));
        }

        [Fact]
        public void ParseMachineList_MemoryAsString_Parses()
        {
            var json = @"[{""Name"":""vm1"",""Memory"":""2147483648"",""DiskSize"":""53687091200""}]";
            var result = InvokeParseMachineList(json);

            Assert.Single(result);
            Assert.Equal(2147483648L, result[0].Memory);
            Assert.Equal(53687091200L, result[0].DiskSize);
        }

        [Fact]
        public void ParseMachineList_AlternateKeys_Works()
        {
            var json = @"[{""name"":""vm1"",""default"":true,""running"":false,""cpus"":2}]";
            var result = InvokeParseMachineList(json);

            Assert.Single(result);
            Assert.Equal("vm1", result[0].Name);
            Assert.True(result[0].Default);
            Assert.Equal(2, result[0].Cpus);
        }

        #endregion

        #region ParseMachineInspect Tests

        [Fact]
        public void ParseMachineInspect_ValidJson_ReturnsDetails()
        {
            var json = @"{
                ""Name"": ""podman-machine-default"",
                ""State"": ""running"",
                ""Rootful"": false,
                ""Created"": ""2026-01-15T10:30:00Z"",
                ""LastUp"": ""2026-02-08T09:00:00Z"",
                ""ConfigDir"": { ""Path"": ""/Users/test/.config/containers/podman/machine"" }
            }";

            var result = InvokeParseMachineInspect(json);

            Assert.Equal("podman-machine-default", result.Name);
            Assert.Equal("running", result.State);
            Assert.False(result.Rootful);
            Assert.Contains("2026", result.Created);
            Assert.Equal("/Users/test/.config/containers/podman/machine", result.ConfigDir);
        }

        [Fact]
        public void ParseMachineInspect_WithResources_ParsesNested()
        {
            var json = @"{
                ""Name"": ""test-vm"",
                ""State"": ""stopped"",
                ""Resources"": {
                    ""CPUs"": 4,
                    ""Memory"": 4096,
                    ""DiskSize"": 100
                },
                ""ConnectionInfo"": {
                    ""PodmanSocket"": {
                        ""Path"": ""/var/folders/xx/podman.sock""
                    }
                }
            }";

            var result = InvokeParseMachineInspect(json);

            Assert.Equal("test-vm", result.Name);
            Assert.NotNull(result.Resources);
            Assert.Equal(4, result.Resources.Cpus);
            Assert.Equal(4096, result.Resources.MemoryMiB);
            Assert.Equal(100, result.Resources.DiskSizeGiB);
            Assert.NotNull(result.ConnectionInfo);
            Assert.Equal("/var/folders/xx/podman.sock", result.ConnectionInfo.PodmanSocketPath);
        }

        [Fact]
        public void ParseMachineInspect_JsonArray_ParsesFirstElement()
        {
            var json = @"[{""Name"":""vm1"",""State"":""running""}]";
            var result = InvokeParseMachineInspect(json);
            Assert.Equal("vm1", result.Name);
            Assert.Equal("running", result.State);
        }

        [Fact]
        public void ParseMachineInspect_EmptyString_ReturnsEmpty()
        {
            var result = InvokeParseMachineInspect("");
            Assert.Null(result.Name);
        }

        #endregion

        #region ParseMachineInfo Tests

        [Fact]
        public void ParseMachineInfo_ValidJson_ReturnsHostInfo()
        {
            var json = @"{
                ""Host"": {
                    ""Arch"": ""arm64"",
                    ""OS"": ""darwin"",
                    ""VMType"": ""applehv"",
                    ""NumberOfMachines"": 2,
                    ""MachineConfigDir"": ""/Users/test/.config/containers/podman/machine""
                },
                ""Version"": {
                    ""APIVersion"": ""5.0.0"",
                    ""Version"": ""5.0.2""
                }
            }";

            var result = InvokeParseMachineInfo(json);

            Assert.Equal("arm64", result.Arch);
            Assert.Equal("darwin", result.OS);
            Assert.Equal("applehv", result.VMType);
            Assert.Equal(2, result.NumberOfMachines);
            Assert.Equal("/Users/test/.config/containers/podman/machine", result.MachineConfigDir);
            Assert.Equal("5.0.0", result.ApiVersion);
            Assert.Equal("5.0.2", result.Version);
        }

        [Fact]
        public void ParseMachineInfo_EmptyString_ReturnsEmpty()
        {
            var result = InvokeParseMachineInfo("");
            Assert.Null(result.Arch);
            Assert.Null(result.Version);
        }

        [Fact]
        public void ParseMachineInfo_WithCurrentMachine_DoesNotOverwriteOS()
        {
            var json = @"{
                ""Host"": {
                    ""Arch"": ""arm64"",
                    ""CurrentMachine"": ""podman-machine-default"",
                    ""OS"": ""darwin"",
                    ""VMType"": ""applehv"",
                    ""NumberOfMachines"": 1
                },
                ""Version"": { ""APIVersion"": ""5.0.0"", ""Version"": ""5.0.2"" }
            }";

            var result = InvokeParseMachineInfo(json);

            Assert.Equal("darwin", result.OS);
            Assert.Equal("podman-machine-default", result.CurrentMachine);
            Assert.Equal("arm64", result.Arch);
        }

        [Fact]
        public void ParseMachineInfo_OnlyCurrentMachine_OSIsNull()
        {
            var json = @"{ ""Host"": { ""CurrentMachine"": ""default"" } }";

            var result = InvokeParseMachineInfo(json);

            Assert.Null(result.OS);
            Assert.Equal("default", result.CurrentMachine);
        }

        [Fact]
        public void ParseMachineInfo_LowercaseOS_FallsBack()
        {
            var json = @"{ ""Host"": { ""os"": ""linux"" } }";
            var result = InvokeParseMachineInfo(json);
            Assert.Equal("linux", result.OS);
        }

        #endregion

        #region Capabilities Tests

        [Fact]
        public void GetCapabilities_SupportsMachines()
        {
            var pack = new PodmanCliDriverPack();
            var caps = pack.GetCapabilitiesAsync().Result;
            Assert.True(caps.SupportsMachines);
        }

        #endregion

        #region Reflection Helpers

        private static string InvokeBuildInitArgs(MachineInitConfig config)
        {
            var method = typeof(PodmanCliMachineDriver).GetMethod(
                "BuildInitArgs",
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            Assert.NotNull(method);
            return (string)method.Invoke(null, new object[] { config });
        }

        private static string InvokeBuildSetArgs(MachineSetConfig config, string name)
        {
            var method = typeof(PodmanCliMachineDriver).GetMethod(
                "BuildSetArgs",
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            Assert.NotNull(method);
            return (string)method.Invoke(null, new object[] { config, name });
        }

        private static IList<MachineInfo> InvokeParseMachineList(string json)
        {
            var method = typeof(PodmanCliMachineDriver).GetMethod(
                "ParseMachineList",
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            Assert.NotNull(method);
            return (IList<MachineInfo>)method.Invoke(null, new object[] { json });
        }

        private static MachineInspectResult InvokeParseMachineInspect(string json)
        {
            var method = typeof(PodmanCliMachineDriver).GetMethod(
                "ParseMachineInspect",
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            Assert.NotNull(method);
            return (MachineInspectResult)method.Invoke(null, new object[] { json });
        }

        private static MachineHostInfo InvokeParseMachineInfo(string json)
        {
            var method = typeof(PodmanCliMachineDriver).GetMethod(
                "ParseMachineInfo",
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            Assert.NotNull(method);
            return (MachineHostInfo)method.Invoke(null, new object[] { json });
        }

        #endregion
    }
}
