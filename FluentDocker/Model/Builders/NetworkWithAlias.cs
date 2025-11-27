using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Model.Builders
{
  public class NetworkWithAlias<T>
  {
    public T Network { get; set; }

    public string Alias { get; set; }

  }
}
