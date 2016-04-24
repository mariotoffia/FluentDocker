using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Extensions
{
  public static class HostExtensions
  {
    public static Result<string> Build(this IHostService host, string tag, string workdir = null,
      ContainerBuildParams prms = null)
    {
      var res = host.Host.Build(tag, workdir, prms, host.Certificates);
      return res.Success ? res.Data[0].ToSuccess() : string.Empty.ToFailure(res.Error, res.Log);
    }
  }
}