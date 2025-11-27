using System.Collections.Generic;
using FluentDocker.Executors;
using FluentDocker.Executors.Parsers;
using FluentDocker.Extensions;
using FluentDocker.Model.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Images;

namespace FluentDocker.Commands
{
  public static class Images
  {
    /// <summary>
    ///   List images. TODO: Not implemented - DO NOT USE THIS METHOD!!
    /// </summary>
    public static CommandResponse<IList<DockerRmImageRowResponse>> Rm(
        this DockerUri host,
        ICertificatePaths certificates = null,
        bool force = false, bool prune = false,
        params string[] imageId)
    {

      // TODO: Need to implement executor properly.
      var options = "";

      if (!prune) {
        options = "--no-prune";
      }

      if (force) {
        options += "--force";
      }

      return
        new ProcessExecutor<ImageRmResponseParser, IList<DockerRmImageRowResponse>>(
          "docker".ResolveBinary(),
          $"{host.RenderBaseArgs(certificates)} images rm {options} {string.Join(" ", imageId)}").Execute();
    }

  }
}