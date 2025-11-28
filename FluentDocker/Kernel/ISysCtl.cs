using FluentDocker.Model.Drivers;

namespace FluentDocker.Kernel
{
    /// <summary>
    /// System control interface for accessing driver components.
    /// Provides methods to resolve driver interfaces by ID.
    /// </summary>
    public interface ISysCtl
    {
        /// <summary>
        /// Gets a driver component interface by driver ID and component enum.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        /// <param name="component">Driver component type</param>
        /// <returns>Driver component instance</returns>
        object SysCtl(string driverId, DriverComponent component);

        /// <summary>
        /// Gets a driver component interface by driver ID and type.
        /// </summary>
        /// <typeparam name="T">Driver component interface (IContainerDriver, IImageDriver, etc.)</typeparam>
        /// <param name="driverId">Driver identifier</param>
        /// <returns>Driver component instance</returns>
        T SysCtl<T>(string driverId) where T : class;
    }
}

