using System;
using System.Threading.Tasks;

namespace FluentDocker.Services
{
  /// <summary>
  /// Hook registration helpers.
  /// </summary>
  public static class ServiceHookExtensions
  {
    /// <summary>
    /// Adds a hook with a generated name and returns that name so it can be removed later.
    /// </summary>
    public static string AddHookWithGeneratedName(
        this IServiceAsync service,
        ServiceRunningState state,
        Func<IServiceAsync, Task> hook)
    {
      ArgumentNullException.ThrowIfNull(service);
      var name = Guid.NewGuid().ToString();
      service.AddHook(state, hook, name);
      return name;
    }
  }
}
