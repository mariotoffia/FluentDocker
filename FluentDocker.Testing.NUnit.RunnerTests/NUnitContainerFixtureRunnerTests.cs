using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Testing.NUnit;
using NUnit.Framework;

namespace FluentDocker.Testing.NUnit.RunnerTests
{
  /// <summary>
  /// Proves NUnit runs <see cref="NUnitContainerFixtureBase"/> against a real daemon.
  /// </summary>
  [TestFixture]
  [Category("Integration")]
  public class NUnitContainerFixtureRunnerTests : NUnitContainerFixtureBase
  {
    protected override void ConfigureContainer(IContainerBuilder builder)
    {
      builder
          .UseImage("busybox:latest")
          .WithCommand("sh", "-c", "while true; do sleep 1; done");
    }

    [Test]
    public async Task FixtureInitializesContainer()
    {
      Assert.That(Container, Is.Not.Null);
      var inspected = await Container.InspectAsync(TestContext.CurrentContext.CancellationToken);
      Assert.That(inspected?.State?.Running, Is.True);
    }
  }
}
