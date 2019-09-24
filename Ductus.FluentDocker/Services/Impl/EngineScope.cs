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
    public bool UseLinux()
    {
      if (this.Scope == EngineScopeType.Linux) return true;

      var success = SwitchToScope(EngineScopeType.Linux);
      if (success)
      {
        Scope = EngineScopeType.Linux;
      }

      return success;
    }

    public bool UseWindows()
    {
      if (this.Scope == EngineScopeType.Windows) return true;

      var success = SwitchToScope(EngineScopeType.Windows);
      if (success)
      {
        Scope = EngineScopeType.Windows;
      }

      return success;
    }

    private bool SwitchToScope(EngineScopeType scope)
    {
      if (scope == EngineScopeType.Linux)
      {
        var result = _host.LinuxDaemon(_certificates);
        return null != result && result.Success;
      }

      var res = _host.WindowsDaemon(_certificates);
      return null != res && res.Success;
    }
  }
}