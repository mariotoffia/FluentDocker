#nullable enable
namespace FluentDocker.Model.Builders
{
  /// <summary>
  /// Marker for a single Dockerfile instruction (e.g. <c>FROM</c>, <c>RUN</c>, <c>COPY</c>) that can
  /// render itself back to its Dockerfile text via <see cref="object.ToString"/>.
  /// </summary>
  public interface ICommand
  {
  }
}
