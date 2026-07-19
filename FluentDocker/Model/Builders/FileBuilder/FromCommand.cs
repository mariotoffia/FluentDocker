#nullable enable
using FluentDocker.Common;
using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  /// <summary>Represents a Dockerfile <c>FROM</c> instruction.</summary>
  public sealed class FromCommand : ICommand
  {
    /// <summary>
    /// Specifies the _FROM_ command.
    /// </summary>
    /// <param name="imageAndTag">The image to derive from and a optional (colon) tag, e.g. myimg:mytag</param>
    /// <param name="asName">An optional alias.</param>
    /// <param name="platform">An optional platform such as linux/amd64 or windows/amd64.</param>
    public FromCommand(TemplateString imageAndTag, TemplateString? asName = null, TemplateString? platform = null)
    {
      if (null == imageAndTag || string.IsNullOrEmpty(imageAndTag.Rendered))
      {
        throw new FluentDockerException("FROM requires at least an image name");
      }

      ImageAndTag = DockerfileInstructionGuard.Validate(imageAndTag.Rendered, "FROM", "image");

      if (null != asName && !string.IsNullOrEmpty(asName.Rendered))
      {
        Alias = DockerfileInstructionGuard.Validate(asName.Rendered, "FROM", "alias");
      }

      if (null != platform && !string.IsNullOrEmpty(platform.Rendered))
      {
        Platform = DockerfileInstructionGuard.Validate(platform.Rendered, "FROM", "platform");
      }
    }

    /// <summary>Gets the base image reference.</summary>
    public string ImageAndTag { get; }
    /// <summary>Gets the optional platform.</summary>
    public string? Platform { get; }
    /// <summary>Gets the optional stage alias.</summary>
    public string? Alias { get; }

    /// <summary>Renders the instruction.</summary>
    public override string ToString()
    {
      var s = "FROM";

      if (!string.IsNullOrEmpty(Platform))
      {
        s = $"{s} --platform={Platform}";
      }

      s = $"{s} {ImageAndTag}";

      if (!string.IsNullOrEmpty(Alias))
      {
        s = $"{s} AS {Alias}";
      }

      return s;
    }
  }
}
