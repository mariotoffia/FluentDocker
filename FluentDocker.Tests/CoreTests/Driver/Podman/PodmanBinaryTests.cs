using System;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
    [Trait("Category", "Unit")]
    public class PodmanBinaryTests
    {
        [Fact]
        public void Translate_Podman_ReturnsPodmanClient()
        {
            var type = PodmanBinary.Translate("podman");
            Assert.Equal(PodmanBinaryType.PodmanClient, type);
        }

        [Fact]
        public void Translate_PodmanExe_ReturnsPodmanClient()
        {
            var type = PodmanBinary.Translate("podman.exe");
            Assert.Equal(PodmanBinaryType.PodmanClient, type);
        }

        [Fact]
        public void Translate_PodmanRemote_ReturnsPodmanRemote()
        {
            var type = PodmanBinary.Translate("podman-remote");
            Assert.Equal(PodmanBinaryType.PodmanRemote, type);
        }

        [Fact]
        public void Translate_PodmanRemoteExe_ReturnsPodmanRemote()
        {
            var type = PodmanBinary.Translate("podman-remote.exe");
            Assert.Equal(PodmanBinaryType.PodmanRemote, type);
        }

        [Fact]
        public void Translate_Unknown_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => PodmanBinary.Translate("docker"));
        }

        [Fact]
        public void FqPath_CombinesPathAndBinary()
        {
            var binary = new PodmanBinary("/usr/bin", "podman", SudoMechanism.None, null);
            Assert.Equal("/usr/bin/podman", binary.FqPath);
        }

        [Fact]
        public void Constructor_SetsProperties()
        {
            var binary = new PodmanBinary(
                "/usr/local/bin", "podman-remote", SudoMechanism.Password, "pass123");

            Assert.Equal("/usr/local/bin", binary.Path);
            Assert.Equal("podman-remote", binary.Binary);
            Assert.Equal(PodmanBinaryType.PodmanRemote, binary.Type);
            Assert.Equal(SudoMechanism.Password, binary.Sudo);
            Assert.Equal("pass123", binary.SudoPassword);
        }

        [Fact]
        public void Constructor_WithExplicitType_OverridesTranslation()
        {
            var binary = new PodmanBinary(
                "/usr/bin", "podman", SudoMechanism.None, null, PodmanBinaryType.PodmanRemote);

            Assert.Equal(PodmanBinaryType.PodmanRemote, binary.Type);
        }

        [Fact]
        public void Constructor_NormalizesBinaryToLowerCase()
        {
            var binary = new PodmanBinary("/usr/bin", "Podman", SudoMechanism.None, null);
            Assert.Equal("podman", binary.Binary);
        }
    }
}
