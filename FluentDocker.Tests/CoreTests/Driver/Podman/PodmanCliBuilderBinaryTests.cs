using System;
using System.IO;
using System.Reflection;
using FluentDocker.Common;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Tests that the public Podman CLI builder surfaces (FIX-9) — <c>WithRequestTimeout</c> and
  /// <c>WithBinary</c> — flow into the <see cref="DriverContext"/>, and that the configured
  /// binary name/search paths are honored by <see cref="PodmanBinariesResolver"/>. The builder
  /// type is internal, so it is exercised via reflection (mirroring the in-repo
  /// TypedDriverBuilderTests helper) to respect the strong-named, no-InternalsVisibleTo rule.
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodmanCliBuilderBinaryTests
  {
    private static DriverContext BuildContext(Action<IPodmanCliDriverBuilder> configure, string driverId = "podman")
    {
      var builderType = typeof(KernelBuilder).Assembly.GetType("FluentDocker.Kernel.PodmanCliDriverBuilder");
      Assert.NotNull(builderType);

      var instance = Activator.CreateInstance(builderType, driverId);
      Assert.NotNull(instance);

      configure((IPodmanCliDriverBuilder)instance);

      var buildMethod = builderType.GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.NotNull(buildMethod);

      var configObj = buildMethod.Invoke(instance, null);
      Assert.NotNull(configObj);

      var contextProp = configObj.GetType().GetProperty("Context");
      Assert.NotNull(contextProp);
      return (DriverContext)contextProp.GetValue(configObj)!;
    }

    [Fact]
    public void WithRequestTimeout_FlowsIntoContext()
    {
      var context = BuildContext(b => b.WithRequestTimeout(TimeSpan.FromSeconds(42)));
      Assert.Equal(TimeSpan.FromSeconds(42), context.RequestTimeout);
    }

    [Fact]
    public void WithBinary_FlowsBinaryNameAndSearchPaths()
    {
      var context = BuildContext(b => b.WithBinary("mypodman", "/opt/x", "/opt/y"));
      Assert.Equal("mypodman", context.BinaryName);
      Assert.Equal(new[] { "/opt/x", "/opt/y" }, context.SearchPaths);
    }

    [Fact]
    public void WithBinary_NoSearchPaths_LeavesSearchPathsNull()
    {
      var context = BuildContext(b => b.WithBinary("mypodman"));
      Assert.Equal("mypodman", context.BinaryName);
      Assert.Null(context.SearchPaths);
    }

    [Fact]
    public void Resolver_HonorsConfiguredBinaryNameAndSearchPath()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("File-based client discovery uses .exe on Windows; covered on POSIX");

      // Deterministic, isolated scratch dir inside the build output (git-ignored), never /tmp.
      var dir = Path.Combine(AppContext.BaseDirectory, $"podman-resolver-{Guid.NewGuid():N}");
      Directory.CreateDirectory(dir);
      try
      {
        File.WriteAllText(Path.Combine(dir, "mypodman"), string.Empty);

        var resolver = new PodmanBinariesResolver(new PodmanBinaryConfiguration
        {
          BinaryName = "mypodman",
          SearchPaths = [dir]
        });

        Assert.NotNull(resolver.MainPodmanClient);
        Assert.Equal("mypodman", resolver.MainPodmanClient.Binary);
        Assert.Equal(dir, resolver.MainPodmanClient.Path);
      }
      finally
      {
        Directory.Delete(dir, recursive: true);
      }
    }

    [Fact]
    public void Resolver_WhenBinaryMissing_ThrowsDriverNotAvailableException()
    {
      var dir = Path.Combine(AppContext.BaseDirectory, ".out", $"podman-resolver-{Guid.NewGuid():N}");
      Directory.CreateDirectory(dir);
      try
      {
        Assert.Throws<DriverNotAvailableException>(() =>
            new PodmanBinariesResolver(new PodmanBinaryConfiguration
            {
              SearchPaths = [dir]
            }));
      }
      finally
      {
        Directory.Delete(dir, recursive: true);
      }
    }
  }
}
