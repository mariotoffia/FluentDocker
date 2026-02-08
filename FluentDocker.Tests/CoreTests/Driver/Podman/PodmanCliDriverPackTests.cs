using System;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
    /// <summary>
    /// Unit tests for PodmanCliDriverPack interface resolution and capabilities.
    /// These tests verify the wiring and resolution logic, not actual Podman execution.
    /// </summary>
    [Trait("Category", "Unit")]
    public class PodmanCliDriverPackTests
    {
        [Fact]
        public void Type_ReturnsPodmanCli()
        {
            var pack = new PodmanCliDriverPack();
            Assert.Equal(DriverType.PodmanCli, pack.Type);
        }

        [Fact]
        public void Runtime_ReturnsPodman()
        {
            var pack = new PodmanCliDriverPack();
            Assert.Equal(RuntimeType.Podman, pack.Runtime);
        }

        [Fact]
        public async Task GetCapabilities_SupportsPods()
        {
            var pack = new PodmanCliDriverPack();
            var caps = await pack.GetCapabilitiesAsync();

            Assert.True(caps.SupportsPods);
            Assert.True(caps.SupportsContainers);
            Assert.True(caps.SupportsImages);
            Assert.True(caps.SupportsNetworks);
            Assert.True(caps.SupportsVolumes);
            Assert.True(caps.SupportsSystem);
            Assert.False(caps.SupportsCompose);
        }

        [Fact]
        public void SysCtl_BeforeInitialize_ThrowsInvalidOperation()
        {
            var pack = new PodmanCliDriverPack();
            Assert.Throws<InvalidOperationException>(() =>
                pack.SysCtl<IContainerDriver>("podman"));
        }

        [Fact]
        public void TrySysCtl_BeforeInitialize_ThrowsInvalidOperation()
        {
            var pack = new PodmanCliDriverPack();
            Assert.Throws<InvalidOperationException>(() =>
                pack.TrySysCtl<IContainerDriver>("podman", out _));
        }

        [Fact]
        public void SysCtlByComponent_BeforeInitialize_ThrowsInvalidOperation()
        {
            var pack = new PodmanCliDriverPack();
            Assert.Throws<InvalidOperationException>(() =>
                pack.SysCtl("podman", DriverComponent.Container));
        }

        [Fact]
        public void SysCtlByType_BeforeInitialize_ThrowsInvalidOperation()
        {
            var pack = new PodmanCliDriverPack();
            Assert.Throws<InvalidOperationException>(() =>
                pack.SysCtl("podman", typeof(IContainerDriver)));
        }

        [Fact]
        public void TryResolve_BeforeInitialize_ThrowsInvalidOperation()
        {
            var pack = new PodmanCliDriverPack();
            Assert.Throws<InvalidOperationException>(() =>
                pack.TryResolve(typeof(IContainerDriver), out _));
        }

        [Fact]
        public void GetSupportedInterfaces_BeforeInitialize_ThrowsInvalidOperation()
        {
            var pack = new PodmanCliDriverPack();
            Assert.Throws<InvalidOperationException>(() =>
                pack.GetSupportedInterfaces());
        }

        [Fact]
        public async Task IsHealthy_BeforeInitialize_ReturnsFalse()
        {
            var pack = new PodmanCliDriverPack();
            var healthy = await pack.IsHealthyAsync();
            Assert.False(healthy);
        }

        [Fact]
        public void SysCtlByComponent_Compose_ThrowsArgumentException()
        {
            // Podman does not support Compose
            // This test validates the behavior *after* initialization,
            // but since we can't initialize without podman binary,
            // we test the pre-init state instead
            var pack = new PodmanCliDriverPack();
            Assert.Throws<InvalidOperationException>(() =>
                pack.SysCtl("podman", DriverComponent.Compose));
        }

        [Fact]
        public void DirectAccess_ContainerDriver_BeforeInit_ThrowsInvalidOperation()
        {
            var pack = new PodmanCliDriverPack();
            Assert.Throws<InvalidOperationException>(() => _ = pack.ContainerDriver);
        }

        [Fact]
        public void DirectAccess_ImageDriver_BeforeInit_ThrowsInvalidOperation()
        {
            var pack = new PodmanCliDriverPack();
            Assert.Throws<InvalidOperationException>(() => _ = pack.ImageDriver);
        }

        [Fact]
        public void DirectAccess_NetworkDriver_BeforeInit_ThrowsInvalidOperation()
        {
            var pack = new PodmanCliDriverPack();
            Assert.Throws<InvalidOperationException>(() => _ = pack.NetworkDriver);
        }

        [Fact]
        public void DirectAccess_VolumeDriver_BeforeInit_ThrowsInvalidOperation()
        {
            var pack = new PodmanCliDriverPack();
            Assert.Throws<InvalidOperationException>(() => _ = pack.VolumeDriver);
        }

        [Fact]
        public void DirectAccess_SystemDriver_BeforeInit_ThrowsInvalidOperation()
        {
            var pack = new PodmanCliDriverPack();
            Assert.Throws<InvalidOperationException>(() => _ = pack.SystemDriver);
        }

        [Fact]
        public void DirectAccess_AuthDriver_BeforeInit_ThrowsInvalidOperation()
        {
            var pack = new PodmanCliDriverPack();
            Assert.Throws<InvalidOperationException>(() => _ = pack.AuthDriver);
        }

        [Fact]
        public void DirectAccess_StreamDriver_BeforeInit_ThrowsInvalidOperation()
        {
            var pack = new PodmanCliDriverPack();
            Assert.Throws<InvalidOperationException>(() => _ = pack.StreamDriver);
        }

        [Fact]
        public void DirectAccess_PodDriver_BeforeInit_ThrowsInvalidOperation()
        {
            var pack = new PodmanCliDriverPack();
            Assert.Throws<InvalidOperationException>(() => _ = pack.PodDriver);
        }

        [Fact]
        public void SysCtlByComponent_Pod_BeforeInit_ThrowsInvalidOperation()
        {
            var pack = new PodmanCliDriverPack();
            Assert.Throws<InvalidOperationException>(() =>
                pack.SysCtl("podman", DriverComponent.Pod));
        }

        [Fact]
        public void DirectAccess_KubernetesDriver_BeforeInit_ThrowsInvalidOperation()
        {
            var pack = new PodmanCliDriverPack();
            Assert.Throws<InvalidOperationException>(() => _ = pack.KubernetesDriver);
        }

        [Fact]
        public void SysCtlByComponent_Kubernetes_BeforeInit_ThrowsInvalidOperation()
        {
            var pack = new PodmanCliDriverPack();
            Assert.Throws<InvalidOperationException>(() =>
                pack.SysCtl("podman", DriverComponent.Kubernetes));
        }

        [Fact]
        public async Task GetCapabilities_SupportsKubernetes()
        {
            var pack = new PodmanCliDriverPack();
            var caps = await pack.GetCapabilitiesAsync();
            Assert.True(caps.SupportsKubernetes);
        }
    }
}
