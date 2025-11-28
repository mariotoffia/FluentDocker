using FluentDocker.Services;

namespace FluentDocker.Model.Builders
{
  public class NetworkWithAlias<T>
  {
    public T Network { get; set; }

    public string Alias { get; set; }

  }
}
