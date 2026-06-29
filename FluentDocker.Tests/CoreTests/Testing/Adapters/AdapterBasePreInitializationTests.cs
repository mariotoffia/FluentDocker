using System;
using FluentDocker.Builders;
using FluentDocker.Testing.MsTest;
using FluentDocker.Testing.NUnit;
using FluentDocker.Testing.Xunit;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing.Adapters
{
  [Trait("Category", "Unit")]
  public class AdapterBasePreInitializationTests
  {
    [Fact]
    public void XunitContainerTestBase_PropertiesBeforeInit_ThrowInvalidOperationException()
    {
      _ = TestContext.Current.CancellationToken;
      var fixture = new TestXunitContainerTestBase();

      Assert.Throws<InvalidOperationException>(() => _ = fixture.Resource);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Container);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Kernel);
    }

    [Fact]
    public void XunitComposeTestBase_PropertiesBeforeInit_ThrowInvalidOperationException()
    {
      _ = TestContext.Current.CancellationToken;
      var fixture = new TestXunitComposeTestBase();

      Assert.Throws<InvalidOperationException>(() => _ = fixture.Resource);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Service);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Kernel);
    }

    [Fact]
    public void XunitTopologyTestBase_PropertiesBeforeInit_ThrowInvalidOperationException()
    {
      _ = TestContext.Current.CancellationToken;
      var fixture = new TestXunitTopologyTestBase();

      Assert.Throws<InvalidOperationException>(() => _ = fixture.Resource);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Kernel);
    }

    [Fact]
    public void XunitContainerFixtureBase_PropertiesBeforeInit_ThrowInvalidOperationException()
    {
      _ = TestContext.Current.CancellationToken;
      var fixture = new TestXunitContainerFixtureBase();

      Assert.Throws<InvalidOperationException>(() => _ = fixture.Resource);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Container);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Kernel);
    }

    [Fact]
    public void XunitComposeFixtureBase_PropertiesBeforeInit_ThrowInvalidOperationException()
    {
      _ = TestContext.Current.CancellationToken;
      var fixture = new TestXunitComposeFixtureBase();

      Assert.Throws<InvalidOperationException>(() => _ = fixture.Resource);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Service);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Kernel);
    }

    [Fact]
    public void XunitTopologyFixtureBase_PropertiesBeforeInit_ThrowInvalidOperationException()
    {
      _ = TestContext.Current.CancellationToken;
      var fixture = new TestXunitTopologyFixtureBase();

      Assert.Throws<InvalidOperationException>(() => _ = fixture.Resource);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Kernel);
    }

    [Fact]
    public void NUnitContainerFixtureBase_PropertiesBeforeInit_ThrowInvalidOperationException()
    {
      _ = TestContext.Current.CancellationToken;
      var fixture = new TestNUnitContainerFixtureBase();

      Assert.Throws<InvalidOperationException>(() => _ = fixture.Resource);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Container);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Kernel);
    }

    [Fact]
    public void MsTestContainerFixtureBase_PropertiesBeforeInit_ThrowInvalidOperationException()
    {
      _ = TestContext.Current.CancellationToken;
      var fixture = new TestMsTestContainerFixtureBase();

      Assert.Throws<InvalidOperationException>(() => _ = fixture.Resource);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Container);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Kernel);
    }

    [Fact]
    public void MsTestContainerFixtureBase_DistinctSubclasses_PreInitAccessIsIndependent()
    {
      _ = TestContext.Current.CancellationToken;
      var first = new TestMsTestContainerFixtureBase();
      var second = new OtherMsTestContainerFixtureBase();

      Assert.Throws<InvalidOperationException>(() => _ = first.Resource);
      Assert.Throws<InvalidOperationException>(() => _ = second.Resource);
      // ponytail: full lifecycle isolation needs a real Docker daemon; public pre-init surface covers no shared initialized state.
    }

    private sealed class TestXunitContainerTestBase : XunitContainerTestBase
    {
      protected override void ConfigureContainer(IContainerBuilder builder)
      {
      }
    }

    private sealed class TestXunitComposeTestBase : XunitComposeTestBase
    {
      protected override void ConfigureCompose(IComposeBuilder builder)
      {
      }
    }

    private sealed class TestXunitTopologyTestBase : XunitTopologyTestBase
    {
      protected override void ConfigureTopology(Builder builder)
      {
      }
    }

    private sealed class TestXunitContainerFixtureBase : XunitContainerFixtureBase
    {
      protected override void ConfigureContainer(IContainerBuilder builder)
      {
      }
    }

    private sealed class TestXunitComposeFixtureBase : XunitComposeFixtureBase
    {
      protected override void ConfigureCompose(IComposeBuilder builder)
      {
      }
    }

    private sealed class TestXunitTopologyFixtureBase : XunitTopologyFixtureBase
    {
      protected override void ConfigureTopology(Builder builder)
      {
      }
    }

    private sealed class TestNUnitContainerFixtureBase : NUnitContainerFixtureBase
    {
      protected override void ConfigureContainer(IContainerBuilder builder)
      {
      }
    }

    private sealed class TestMsTestContainerFixtureBase : MsTestContainerFixtureBase
    {
      protected override void ConfigureContainer(IContainerBuilder builder)
      {
      }
    }

    private sealed class OtherMsTestContainerFixtureBase : MsTestContainerFixtureBase
    {
      protected override void ConfigureContainer(IContainerBuilder builder)
      {
      }
    }
  }
}
