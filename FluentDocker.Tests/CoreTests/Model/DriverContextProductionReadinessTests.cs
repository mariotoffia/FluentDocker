using System.Text.Json;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  [Trait("Category", "Unit")]
  public class DriverContextProductionReadinessTests
  {
    [Fact]
    public void SerializeAndToString_RedactSudoPassword()
    {
      var context = new DriverContext("docker")
      {
        Sudo = SudoMechanism.Password,
        SudoPassword = "super-secret"
      };

      var json = JsonSerializer.Serialize(context);
      var text = context.ToString();

      Assert.DoesNotContain("super-secret", json);
      Assert.DoesNotContain("super-secret", text);
      Assert.DoesNotContain("SudoPassword", json);
      Assert.Contains("SudoPassword=***", text);
    }
  }
}
