namespace FluentDocker.Builders
{
  public partial interface IContainerBuilder
  {
    /// <summary>Adds a container health check.</summary>
    /// <param name="cmd">Shell command that returns 0 when healthy.</param>
    /// <param name="interval">Check interval (for example, <c>5s</c>).</param>
    /// <param name="timeout">Check timeout (for example, <c>1s</c>).</param>
    /// <param name="retries">Retries before marking unhealthy.</param>
    /// <param name="startPeriod">Start period before health checks count.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithHealthCheck(
        string cmd, string? interval = null, string? timeout = null,
        int retries = 0, string? startPeriod = null) =>
        throw new System.NotSupportedException("This IContainerBuilder implementation does not support WithHealthCheck.");

    /// <summary>Adds DNS servers to the container.</summary>
    /// <param name="servers">DNS server IP addresses.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithDns(params string[] servers) =>
        throw new System.NotSupportedException("This IContainerBuilder implementation does not support WithDns.");

    /// <summary>Sets the stop signal sent to the container process.</summary>
    /// <param name="signal">Signal name or number, for example <c>SIGTERM</c>.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithStopSignal(string signal) =>
        throw new System.NotSupportedException("This IContainerBuilder implementation does not support WithStopSignal.");
  }
}
