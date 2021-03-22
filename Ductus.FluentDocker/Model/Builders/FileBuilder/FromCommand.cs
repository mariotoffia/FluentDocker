using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Common;

namespace Ductus.FluentDocker.Model.Builders.FileBuilder
{
  public sealed class FromCommand : ICommand
  {
    /// <summary>
    /// Specifies the _FROM_ command.
    /// </summary>
    /// <param name="imageAndTag">The image to derive from and a optional (colon) tag, e.g. myimg:mytag</param>
    /// <param name="asName">An optional alias.</param>
    /// <param name="platform">An optional platform such linux/amd64 or windows/amd64.</param>
    public FromCommand(TemplateString imageAndTag, TemplateString asName = null, TemplateString platform = null)
    {
      if (null == imageAndTag || string.IsNullOrEmpty(imageAndTag.Rendered)) {
        throw new FluentDockerException("FROM requires atleast a image name");
      }

      ImageAndTag = imageAndTag;

      if (null != asName && !string.IsNullOrEmpty(asName.Rendered))
      {
        Alias = asName.Rendered;
      }

      if (null != platform && !string.IsNullOrEmpty(platform.Rendered))
      {
        Platform = platform.Rendered;
      }
    }

    public string ImageAndTag { get; }
    public string Platform { get; }
    public string Alias { get; }

    public override string ToString()
    {
      var s = "FROM";

      if (!string.IsNullOrEmpty(Platform)) {
        s = $"{s} --platform={Platform}";
      }

      s = $"{s} {ImageAndTag}";

      if (!string.IsNullOrEmpty(Alias)) {
        s = $"{s} AS {Alias}";
      }

      return s;
    }
  }
}
