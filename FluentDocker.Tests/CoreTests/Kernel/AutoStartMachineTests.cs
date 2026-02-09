using System;
using System.Reflection;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Kernel
{
    /// <summary>
    /// Unit tests for WithAutoStartMachine feature — config model,
    /// builder propagation, and DriverContext integration.
    /// </summary>
    [Trait("Category", "Unit")]
    public class AutoStartMachineTests
    {
        #region AutoStartMachineConfig Defaults

        [Fact]
        public void AutoStartMachineConfig_Defaults_AreCorrect()
        {
            var config = new AutoStartMachineConfig();

            Assert.Null(config.MachineName);
            Assert.False(config.CreateIfNotExists);
            Assert.Null(config.InitCpus);
            Assert.Null(config.InitMemoryMiB);
            Assert.Null(config.InitDiskSizeGiB);
            Assert.False(config.InitRootful);
        }

        [Fact]
        public void AutoStartMachineConfig_AllProperties_CanBeSet()
        {
            var config = new AutoStartMachineConfig
            {
                MachineName = "my-machine",
                CreateIfNotExists = true,
                InitCpus = 4,
                InitMemoryMiB = 4096,
                InitDiskSizeGiB = 100,
                InitRootful = true
            };

            Assert.Equal("my-machine", config.MachineName);
            Assert.True(config.CreateIfNotExists);
            Assert.Equal(4, config.InitCpus);
            Assert.Equal(4096, config.InitMemoryMiB);
            Assert.Equal(100, config.InitDiskSizeGiB);
            Assert.True(config.InitRootful);
        }

        #endregion

        #region DriverContext Integration

        [Fact]
        public void DriverContext_AutoStartMachine_NullByDefault()
        {
            var context = new DriverContext("test");
            Assert.Null(context.AutoStartMachine);
        }

        [Fact]
        public void DriverContext_AutoStartMachine_CanBeSet()
        {
            var config = new AutoStartMachineConfig { MachineName = "dev" };
            var context = new DriverContext("test")
            {
                AutoStartMachine = config
            };

            Assert.NotNull(context.AutoStartMachine);
            Assert.Equal("dev", context.AutoStartMachine.MachineName);
        }

        #endregion

        #region IPodmanCliDriverBuilder WithAutoStartMachine

        [Fact]
        public void WithAutoStartMachine_NoAction_SetsDefaultConfig()
        {
            // Build a driver configuration using reflection to access internal Build()
            var config = BuildPodmanDriverConfig(b => b
                .WithAutoStartMachine());

            Assert.NotNull(config.AutoStartMachine);
            Assert.Null(config.AutoStartMachine.MachineName);
            Assert.False(config.AutoStartMachine.CreateIfNotExists);
        }

        [Fact]
        public void WithAutoStartMachine_WithConfigure_SetsProperties()
        {
            var config = BuildPodmanDriverConfig(b => b
                .WithAutoStartMachine(c =>
                {
                    c.MachineName = "custom";
                    c.CreateIfNotExists = true;
                    c.InitCpus = 2;
                    c.InitMemoryMiB = 2048;
                    c.InitDiskSizeGiB = 50;
                    c.InitRootful = true;
                }));

            Assert.NotNull(config.AutoStartMachine);
            Assert.Equal("custom", config.AutoStartMachine.MachineName);
            Assert.True(config.AutoStartMachine.CreateIfNotExists);
            Assert.Equal(2, config.AutoStartMachine.InitCpus);
            Assert.Equal(2048, config.AutoStartMachine.InitMemoryMiB);
            Assert.Equal(50, config.AutoStartMachine.InitDiskSizeGiB);
            Assert.True(config.AutoStartMachine.InitRootful);
        }

        [Fact]
        public void WithAutoStartMachine_NullAction_SetsDefaultConfig()
        {
            var config = BuildPodmanDriverConfig(b => b
                .WithAutoStartMachine(null));

            Assert.NotNull(config.AutoStartMachine);
            Assert.Null(config.AutoStartMachine.MachineName);
            Assert.False(config.AutoStartMachine.CreateIfNotExists);
        }

        [Fact]
        public void WithoutAutoStartMachine_ConfigIsNull()
        {
            var config = BuildPodmanDriverConfig(_ => { });

            Assert.Null(config.AutoStartMachine);
        }

        [Fact]
        public void WithAutoStartMachine_ChainsCorrectly()
        {
            var config = BuildPodmanDriverConfig(b => b
                .WithAutoStartMachine()
                .AsDefault());

            Assert.NotNull(config.AutoStartMachine);
            Assert.True(config.IsDefault);
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Uses reflection to create and build a PodmanCliDriverBuilder (internal class)
        /// and extract the DriverContext from the resulting DriverConfiguration.
        /// </summary>
        private static DriverConfigResult BuildPodmanDriverConfig(Action<IPodmanCliDriverBuilder> configure)
        {
            var driverBuilderType = typeof(KernelBuilder).Assembly
                .GetType("FluentDocker.Kernel.PodmanCliDriverBuilder");
            Assert.NotNull(driverBuilderType);

            var instance = Activator.CreateInstance(driverBuilderType, "test-driver");
            Assert.NotNull(instance);

            var builder = (IPodmanCliDriverBuilder)instance;
            configure(builder);

            var buildMethod = driverBuilderType.GetMethod("Build",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(buildMethod);

            var configObj = buildMethod.Invoke(instance, null);
            Assert.NotNull(configObj);

            var contextProp = configObj.GetType().GetProperty("Context");
            var isDefaultProp = configObj.GetType().GetProperty("IsDefault");

            var context = (DriverContext)contextProp?.GetValue(configObj);
            var isDefault = (bool)(isDefaultProp?.GetValue(configObj) ?? false);

            return new DriverConfigResult
            {
                AutoStartMachine = context?.AutoStartMachine,
                IsDefault = isDefault
            };
        }

        private class DriverConfigResult
        {
            public AutoStartMachineConfig AutoStartMachine { get; set; }
            public bool IsDefault { get; set; }
        }

        #endregion
    }
}
