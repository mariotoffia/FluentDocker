using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Tests.Mocks
{
    /// <summary>
    /// Extension methods for kernel builder to support mock drivers.
    /// </summary>
    public static class MockKernelBuilderExtensions
    {
        /// <summary>
        /// Creates a kernel with a mock driver pack for testing.
        /// </summary>
        public static async Task<(FluentDockerKernel kernel, MockDriverPack mockPack)> CreateWithMockDriverAsync(
            string driverId = "docker",
            bool asDefault = true)
        {
            var mockPack = new MockDriverPack();
            var context = new DriverContext(driverId);
            await mockPack.InitializeAsync(context);

            var kernel = new FluentDockerKernel();
            await kernel.RegisterDriverPackAsync(driverId, mockPack, context);

            if (asDefault)
            {
                kernel.SetDefaultDriver(driverId);
            }

            return (kernel, mockPack);
        }

        /// <summary>
        /// Creates a kernel with a pre-configured mock driver pack.
        /// </summary>
        public static async Task<FluentDockerKernel> CreateWithMockDriverAsync(
            string driverId,
            MockDriverPack mockPack,
            bool asDefault = true)
        {
            var context = new DriverContext(driverId);
            await mockPack.InitializeAsync(context);

            var kernel = new FluentDockerKernel();
            await kernel.RegisterDriverPackAsync(driverId, mockPack, context);

            if (asDefault)
            {
                kernel.SetDefaultDriver(driverId);
            }

            return kernel;
        }
    }

    /// <summary>
    /// Test fixture base class for unit tests that need a mock kernel.
    /// </summary>
    public abstract class MockKernelTestBase : IAsyncDisposable
    {
        protected FluentDockerKernel Kernel { get; private set; }
        protected MockDriverPack MockPack { get; private set; }
        protected string DriverId { get; private set; } = "docker";

        /// <summary>
        /// Initializes the test with a mock kernel.
        /// </summary>
        protected async Task InitializeMockKernelAsync(string driverId = "docker")
        {
            DriverId = driverId;
            (Kernel, MockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync(driverId);
        }

        /// <summary>
        /// Initializes the test with a pre-configured mock pack.
        /// </summary>
        protected async Task InitializeMockKernelAsync(MockDriverPack mockPack, string driverId = "docker")
        {
            DriverId = driverId;
            MockPack = mockPack;
            Kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync(driverId, mockPack);
        }

        public ValueTask DisposeAsync()
        {
            Kernel?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
