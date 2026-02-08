namespace FluentDocker.Builders
{
    /// <summary>
    /// Extension methods for IDriverScopedBuilder to resolve driver-specific interfaces.
    /// These enable driver-aware builder extensions without coupling to specific drivers.
    /// </summary>
    public static class DriverScopedBuilderExtensions
    {
        /// <summary>
        /// Resolves a driver interface from the builder's kernel context.
        /// Throws InterfaceNotSupportedException if not available.
        /// </summary>
        /// <typeparam name="T">Driver interface type to resolve</typeparam>
        /// <param name="builder">The driver-scoped builder</param>
        /// <returns>The resolved driver interface</returns>
        public static T RequireDriver<T>(this IDriverScopedBuilder builder) where T : class
            => builder.Kernel.SysCtl<T>(builder.DriverId);

        /// <summary>
        /// Attempts to resolve a driver interface from the builder's kernel context.
        /// Returns null if the interface is not supported by the current driver.
        /// </summary>
        /// <typeparam name="T">Driver interface type to resolve</typeparam>
        /// <param name="builder">The driver-scoped builder</param>
        /// <returns>The resolved driver interface, or null if not supported</returns>
        public static T TryDriver<T>(this IDriverScopedBuilder builder) where T : class
            => builder.Kernel.TrySysCtl<T>(builder.DriverId, out var v) ? v : null;
    }
}
