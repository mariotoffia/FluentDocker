using System.Collections.Generic;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Executors.Parsers;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Images;

namespace Ductus.FluentDocker.Commands
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
