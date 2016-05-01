using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Services.Extensions
{
  public static class HostExtensions
  {
    public static Result<string> Build(this IHostService host, string name, string tag, string workdir = null,
      ContainerBuildParams prms = null)
    {
      var res = host.Host.Build(name, tag, workdir, prms, host.Certificates);
      return res.Success ? res.Data[0].ToSuccess() : string.Empty.ToFailure(res.Error, res.Log);
    }
  }
}