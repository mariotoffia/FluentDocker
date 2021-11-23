using System.IO;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker;

namespace Simple.Compose
{
  public class Sample
  {
    public static void Runner()
    {

      var file = Path.Combine(Directory.GetCurrentDirectory(), (TemplateString)"Resources", "docker-compose.yml");

      using (var svc = Fd.UseContainer()
                            .UseCompose()
                            .ForceRecreate()
                            .ServiceName("test-services")
                            .FromFile(file)
                            .RemoveOrphans()
                            .ForceRecreate()
                            .Build()
                            .Start())
      {

      }
    }
  }
}
