using FluentDocker.Model.Containers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  [Trait("Category", "Unit")]
  public class VolumeMountTests
  {
    [Fact]
    public void ToString_ReadWriteMount_HasNoTrailingSpace()
    {
      var mount = new VolumeMount
      {
        Source = "/host",
        Destination = "/container",
        Rw = true
      };

      Assert.Equal("/host:/container:rw", mount.ToString());
    }
  }
}
