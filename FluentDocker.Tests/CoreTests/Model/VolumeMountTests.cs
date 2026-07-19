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

    [Fact]
    public void ToString_ModeAndAccess_AreCommaJoinedDockerFlags()
    {
      var mount = new VolumeMount
      {
        Source = "/host",
        Destination = "/container",
        Mode = "Z",
        Rw = true
      };

      Assert.Equal("/host:/container:Z,rw", mount.ToString());
    }

    // ML-6: a DTO without a destination cannot produce a valid -v spec; it must render as an
    // empty string instead of an invalid (and read-only-defaulting) fragment.
    [Fact]
    public void ToString_AllFieldsAbsent_ReturnsEmpty()
    {
      Assert.Equal(string.Empty, new VolumeMount().ToString());
    }

    [Fact]
    public void ToString_SourceOnly_ReturnsEmpty()
    {
      var mount = new VolumeMount { Source = "/host" };

      Assert.Equal(string.Empty, mount.ToString());
    }

    [Fact]
    public void ToString_DestinationOnly_RendersDestinationWithAccessSuffix()
    {
      var mount = new VolumeMount { Destination = "/container", Rw = true };

      Assert.Equal("/container:rw", mount.ToString());
    }

    [Fact]
    public void ToString_DestinationOnlyDefaultRw_RendersReadOnly()
    {
      // Rw is a plain bool: absent and explicit read-only are indistinguishable, so the
      // default renders "ro" (documented on VolumeMount.ToString()).
      var mount = new VolumeMount { Destination = "/container" };

      Assert.Equal("/container:ro", mount.ToString());
    }
  }
}
