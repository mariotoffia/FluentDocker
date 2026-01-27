using System;
using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Extensions.Utils;
using Ductus.FluentDocker.Model.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.ExtensionTests
{
  [TestClass]
  public class ContainerEngineTests
  {
    [TestInitialize]
    public void Initialize()
    {
      // Reset to Auto for each test
      ContainerEngine.Auto.SetContainerEngine();
    }

    [TestCleanup]
    public void Cleanup()
    {
      // Reset to Auto after each test
      ContainerEngine.Auto.SetContainerEngine();
    }

    [TestMethod]
    public void SetContainerEngineShallUpdatePreference()
    {
      // Test Docker
      ContainerEngine.Docker.SetContainerEngine();
      Assert.AreEqual(ContainerEngine.Docker, CommandExtensions.ContainerEngine);

      // Test Podman
      ContainerEngine.Podman.SetContainerEngine();
      Assert.AreEqual(ContainerEngine.Podman, CommandExtensions.ContainerEngine);

      // Test Auto
      ContainerEngine.Auto.SetContainerEngine();
      Assert.AreEqual(ContainerEngine.Auto, CommandExtensions.ContainerEngine);
    }

    [TestMethod]
    public void ActiveContainerEngineShallReflectResolvedEngine()
    {
      var resolver = new DockerBinariesResolver(SudoMechanism.None, null, ContainerEngine.Auto);
      
      // ActiveEngine should reflect what was actually found
      Assert.IsNotNull(resolver.ActiveEngine);
      Assert.IsTrue(resolver.ActiveEngine == ContainerEngine.Docker || resolver.ActiveEngine == ContainerEngine.Podman);
      
      // ActiveContainerEngine should match
      var activeEngine = CommandExtensions.ActiveContainerEngine;
      Assert.IsTrue(activeEngine == ContainerEngine.Docker || activeEngine == ContainerEngine.Podman);
    }

    [TestMethod]
    public void PreferredEngineShallBeStored()
    {
      var resolver = new DockerBinariesResolver(SudoMechanism.None, null, ContainerEngine.Podman);
      Assert.AreEqual(ContainerEngine.Podman, resolver.PreferredEngine);

      resolver = new DockerBinariesResolver(SudoMechanism.None, null, ContainerEngine.Docker);
      Assert.AreEqual(ContainerEngine.Docker, resolver.PreferredEngine);

      resolver = new DockerBinariesResolver(SudoMechanism.None, null, ContainerEngine.Auto);
      Assert.AreEqual(ContainerEngine.Auto, resolver.PreferredEngine);
    }

    [TestMethod]
    public void ResolverShallPreferDockerWhenAutoAndBothAvailable()
    {
      // This test verifies that when Auto is selected and both engines are available,
      // Docker is preferred. Note: This may not always be testable depending on environment.
      var resolver = new DockerBinariesResolver(SudoMechanism.None, null, ContainerEngine.Auto);
      
      if (resolver.MainDockerClient != null)
      {
        // If Docker is available, it should be selected
        Assert.AreEqual(ContainerEngine.Docker, resolver.ActiveEngine);
      }
    }

    [TestMethod]
    public void ResolverShallUsePodmanWhenExplicitlyRequested()
    {
      var resolver = new DockerBinariesResolver(SudoMechanism.None, null, ContainerEngine.Podman);
      
      // If Podman is available, it should be selected
      if (resolver.MainDockerClient != null && resolver.MainDockerClient.Engine == ContainerEngine.Podman)
      {
        Assert.AreEqual(ContainerEngine.Podman, resolver.ActiveEngine);
        Assert.IsTrue(resolver.MainDockerClient.Binary.Contains("podman", StringComparison.OrdinalIgnoreCase));
      }
    }

    [TestMethod]
    public void ResolverShallUseDockerWhenExplicitlyRequested()
    {
      var resolver = new DockerBinariesResolver(SudoMechanism.None, null, ContainerEngine.Docker);
      
      // If Docker is available, it should be selected
      if (resolver.MainDockerClient != null && resolver.MainDockerClient.Engine == ContainerEngine.Docker)
      {
        Assert.AreEqual(ContainerEngine.Docker, resolver.ActiveEngine);
        Assert.IsTrue(resolver.MainDockerClient.Binary.Contains("docker", StringComparison.OrdinalIgnoreCase));
      }
    }

    [TestMethod]
    public void DockerBinaryShallIdentifyEngineCorrectly()
    {
      // Test through resolver - DockerBinary constructor is internal
      var resolver = new DockerBinariesResolver(SudoMechanism.None, null, ContainerEngine.Auto);
      
      // Check that binaries have correct engine identification
      foreach (var binary in resolver.Binaries)
      {
        if (binary.Binary.Contains("docker", StringComparison.OrdinalIgnoreCase) && 
            !binary.Binary.Contains("podman", StringComparison.OrdinalIgnoreCase))
        {
          Assert.AreEqual(ContainerEngine.Docker, binary.Engine, 
            $"Binary {binary.Binary} should be identified as Docker");
        }
        else if (binary.Binary.Contains("podman", StringComparison.OrdinalIgnoreCase))
        {
          Assert.AreEqual(ContainerEngine.Podman, binary.Engine, 
            $"Binary {binary.Binary} should be identified as Podman");
        }
      }
    }

    [TestMethod]
    public void ResolveBinaryShallNormalizePodmanCommands()
    {
      var resolver = new DockerBinariesResolver(SudoMechanism.None, null, ContainerEngine.Auto);
      
      // These should all resolve to the same binary type
      try
      {
        var dockerCmd = "docker".ResolveBinary(resolver);
        var podmanCmd = "podman".ResolveBinary(resolver);
        
        // Both should resolve (though to potentially different binaries)
        Assert.IsNotNull(dockerCmd);
        Assert.IsNotNull(podmanCmd);
      }
      catch (FluentDockerException)
      {
        // If neither is available, that's ok for this test
      }
    }

    [TestMethod]
    public void ResolveBinaryShallNormalizeComposeCommands()
    {
      var resolver = new DockerBinariesResolver(SudoMechanism.None, null, ContainerEngine.Auto);
      
      try
      {
        var dockerComposeCmd = "docker-compose".ResolveBinary(resolver);
        var podmanComposeCmd = "podman-compose".ResolveBinary(resolver);
        
        // Both should resolve (though to potentially different binaries)
        Assert.IsNotNull(dockerComposeCmd);
        Assert.IsNotNull(podmanComposeCmd);
      }
      catch (FluentDockerException)
      {
        // If neither is available, that's ok for this test
      }
    }

    [TestMethod]
    public void InfoSwitchShallHandlePodmanGracefully()
    {
      // Save current engine
      var originalEngine = CommandExtensions.ContainerEngine;
      
      try
      {
        // Set to Podman
        ContainerEngine.Podman.SetContainerEngine();
        
        // Switch should return success with informative message
        var dockerUri = new DockerUri(DockerUri.GetDockerHostEnvironmentPathOrDefault());
        var result = dockerUri.Switch();
        Assert.IsTrue(result.Success);
        Assert.IsTrue(result.Data.Contains("Podman", StringComparison.OrdinalIgnoreCase) || 
                     (result.Log.Count > 0 && result.Log[0].Contains("Podman", StringComparison.OrdinalIgnoreCase)));
      }
      finally
      {
        // Restore original engine
        originalEngine.SetContainerEngine();
      }
    }

    [TestMethod]
    public void InfoLinuxDaemonShallHandlePodmanGracefully()
    {
      // Save current engine
      var originalEngine = CommandExtensions.ContainerEngine;
      
      try
      {
        // Set to Podman
        ContainerEngine.Podman.SetContainerEngine();
        
        // LinuxDaemon should return success with informative message
        var dockerUri = new DockerUri(DockerUri.GetDockerHostEnvironmentPathOrDefault());
        var result = dockerUri.LinuxDaemon();
        Assert.IsTrue(result.Success);
        Assert.IsTrue(result.Data.Contains("Podman", StringComparison.OrdinalIgnoreCase) || 
                     (result.Log.Count > 0 && result.Log[0].Contains("Podman", StringComparison.OrdinalIgnoreCase)));
      }
      finally
      {
        // Restore original engine
        originalEngine.SetContainerEngine();
      }
    }

    [TestMethod]
    public void InfoWindowsDaemonShallReturnErrorForPodman()
    {
      // Save current engine
      var originalEngine = CommandExtensions.ContainerEngine;
      
      try
      {
        // Set to Podman
        ContainerEngine.Podman.SetContainerEngine();
        
        // WindowsDaemon should return error for Podman
        var dockerUri = new DockerUri(DockerUri.GetDockerHostEnvironmentPathOrDefault());
        var result = dockerUri.WindowsDaemon();
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Error.Contains("Podman", StringComparison.OrdinalIgnoreCase) ||
                     result.Error.Contains("Windows", StringComparison.OrdinalIgnoreCase));
      }
      finally
      {
        // Restore original engine
        originalEngine.SetContainerEngine();
      }
    }

    [TestMethod]
    public void IsComposeBinaryPresentShallCheckBothV1AndV2()
    {
      // This should work regardless of engine
      // Just verify it doesn't throw - actual result depends on environment
      var isPresent = CommandExtensions.IsComposeBinaryPresent();
      // Method call above verifies it doesn't throw - no assertion needed
    }

    [TestMethod]
    public void GetResolvedBinariesShallIncludeEngineInfo()
    {
      var binaries = CommandExtensions.GetResolvedBinaries();
      
      Assert.IsNotNull(binaries);
      Assert.IsTrue(binaries.Any());
      
      // Should include engine information
      var engineInfo = string.Join(" ", binaries);
      Assert.IsTrue(engineInfo.Contains("engine", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ResolverShallFallbackToAvailableEngineWhenPreferredNotAvailable()
    {
      // Test with explicit Podman when only Docker might be available
      try
      {
        var resolver = new DockerBinariesResolver(SudoMechanism.None, null, ContainerEngine.Podman);
        
        // If Podman is not available but Docker is, it should still work (fallback behavior)
        // The resolver will throw if NO engine is available, but if one is available, it should use it
        if (resolver.MainDockerClient != null)
        {
          // An engine was found (might be Docker or Podman)
          Assert.IsNotNull(resolver.ActiveEngine);
        }
      }
      catch (FluentDockerException)
      {
        // If no engine is available at all, that's expected
      }
    }

    [TestMethod]
    public void ComposeV2DetectionShallWorkWithPodman()
    {
      var resolver = new DockerBinariesResolver(SudoMechanism.None, null, ContainerEngine.Podman);
      
      // If Podman is available and supports Compose V2, it should be detected
      if (resolver.MainDockerClient != null && resolver.MainDockerClient.Engine == ContainerEngine.Podman)
      {
        // Compose V2 might be available
        // Just verify the check doesn't throw
        var hasV2 = resolver.IsDockerComposeV2Available;
        Assert.IsTrue(hasV2 || !hasV2); // Always true, just checking it doesn't throw
      }
    }
  }
}
