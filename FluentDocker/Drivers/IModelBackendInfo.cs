using System.Collections.Generic;

namespace FluentDocker.Drivers
{
  /// <summary>
  /// Optional, additive capability a model driver MAY implement to advertise the
  /// inference backend engine(s) it runs models on (for example <c>llama.cpp</c>,
  /// <c>vllm</c>, or <c>mlx</c>).
  /// </summary>
  /// <remarks>
  /// <para>
  /// This interface is entirely opt-in: a model driver that does not implement it
  /// makes no claim about an inference backend. It exists so the runner can source
  /// its reported backend from the driver that actually owns that knowledge, instead
  /// of assuming a hardcoded engine.
  /// </para>
  /// <para>
  /// When no resolvable driver backing a runner implements this interface, the runner
  /// reports no backend — <c>ModelRunnerCapabilities.DefaultBackend</c> is <c>null</c>
  /// and <c>ModelRunnerCapabilities.AvailableBackends</c> is empty — rather than
  /// defaulting to a particular engine. The Docker Model Runner CLI adapter implements
  /// it to preserve its historical <c>llama.cpp</c> backend.
  /// </para>
  /// </remarks>
  public interface IModelBackendInfo
  {
    /// <summary>
    /// The backend engine used by default for inference (for example <c>llama.cpp</c>).
    /// </summary>
    string DefaultBackend { get; }

    /// <summary>
    /// All inference backend engines this driver can run models on. Never <c>null</c>;
    /// for a driver that advertises a backend it contains at least
    /// <see cref="DefaultBackend"/>.
    /// </summary>
    IReadOnlyList<string> AvailableBackends { get; }
  }
}
