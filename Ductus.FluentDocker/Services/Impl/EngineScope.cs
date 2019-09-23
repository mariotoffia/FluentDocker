using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Services.Impl
{
  /// <summary>
  /// Default Implementation of <see cref="IEngineScope" />.
  /// </summary>
  internal class EngineScope : IEngineScope
  {
    private readonly EngineScopeType _original;
    private readonly DockerUri _host;
    private readonly ICertificatePaths _certificates;
    internal EngineScope(DockerUri host, EngineScopeType scope, ICertificatePaths certificates = null)
    {
      Scope = scope;
      _host = host;
      _certificates = certificates;
      
      _original = host.IsWindowsEngine(certificates) ? EngineScopeType.Windows : EngineScopeType.Linux;

      if (scope == _original) return;

      SwitchToScope(Scope);
    }

    public void Dispose()
    {
      if (_original == Scope) return;
      SwitchToScope(_original);
    }

    public EngineScopeType Scope { get; private set; }

    private void SwitchToScope(EngineScopeType scope)
    {
      if (scope == EngineScopeType.Linux)
      {
        _host.LinuxDaemon(_certificates);
        return;
      }

      _host.WindowsDaemon(_certificates);
    }
  }
}