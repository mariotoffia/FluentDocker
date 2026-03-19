using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using Xunit;

namespace FluentDocker.Tests.Integration.PodmanCliDriver
{
  [Collection("PodmanDriver")]
  [Trait("Category", "PodmanIntegration")]
  public class PodmanKubernetesDriverTests : PodmanDriverTestBase
  {
    private const string SimplePodYaml = @"
apiVersion: v1
kind: Pod
metadata:
  name: test-kube-play
spec:
  containers:
    - name: alpine
      image: alpine:latest
      command: [""sleep"", ""300""]
";

    #region Play and Down

    [Fact]
    public async Task PlayAndDown_SimplePod_Succeeds()
    {
      string? yamlPath = null;
      try
      {
        yamlPath = CreateTempYaml(SimplePodYaml);
        await EnsureImageAsync(TestImage);

        var playResult = await KubernetesDriver.PlayAsync(
            Context, new KubePlayConfig { YamlPath = yamlPath }, TestContext.Current.CancellationToken);

        Assert.True(playResult.Success, $"Play failed: {playResult.Error}");
        Assert.NotNull(playResult.Data);
        Assert.NotEmpty(playResult.Data.Pods);
      }
      finally
      {
        await CleanupYaml(yamlPath);
      }
    }

    [Fact]
    public async Task Play_WithReplace_Succeeds()
    {
      string? yamlPath = null;
      try
      {
        yamlPath = CreateTempYaml(SimplePodYaml);
        await EnsureImageAsync(TestImage);

        // First play
        var firstResult = await KubernetesDriver.PlayAsync(
            Context, new KubePlayConfig { YamlPath = yamlPath }, TestContext.Current.CancellationToken);
        Assert.True(firstResult.Success, $"First play failed: {firstResult.Error}");

        // Second play with replace
        var replaceResult = await KubernetesDriver.PlayAsync(
            Context, new KubePlayConfig
            {
              YamlPath = yamlPath,
              Replace = true
            }, TestContext.Current.CancellationToken);
        Assert.True(replaceResult.Success, $"Replace play failed: {replaceResult.Error}");
      }
      finally
      {
        await CleanupYaml(yamlPath);
      }
    }

    [Fact]
    public async Task Play_WithNoStart_CreatesButDoesNotStart()
    {
      string? yamlPath = null;
      try
      {
        yamlPath = CreateTempYaml(SimplePodYaml);
        await EnsureImageAsync(TestImage);

        var playResult = await KubernetesDriver.PlayAsync(
            Context, new KubePlayConfig
            {
              YamlPath = yamlPath,
              Start = false
            }, TestContext.Current.CancellationToken);

        Assert.True(playResult.Success, $"Play failed: {playResult.Error}");
        Assert.NotNull(playResult.Data);
        Assert.NotEmpty(playResult.Data.Pods);

        // Verify the pod is NOT in Running state
        var pods = await PodDriver.ListPodsAsync(Context, TestContext.Current.CancellationToken);
        Assert.True(pods.Success, $"ListPods failed: {pods.Error}");
        var createdPod = pods.Data.FirstOrDefault(
            p => p.Name != null && p.Name.Contains("test-kube-play"));
        Assert.NotNull(createdPod);
        Assert.NotEqual("Running", createdPod.Status);
      }
      finally
      {
        await CleanupYaml(yamlPath);
      }
    }

    [Fact]
    public async Task Down_AfterPlay_RemovesResources()
    {
      string? yamlPath = null;
      try
      {
        yamlPath = CreateTempYaml(SimplePodYaml);
        await EnsureImageAsync(TestImage);

        var playResult = await KubernetesDriver.PlayAsync(
            Context, new KubePlayConfig { YamlPath = yamlPath }, TestContext.Current.CancellationToken);
        Assert.True(playResult.Success, $"Play failed: {playResult.Error}");

        var downResult = await KubernetesDriver.DownAsync(Context, yamlPath, TestContext.Current.CancellationToken);
        Assert.True(downResult.Success, $"Down failed: {downResult.Error}");
      }
      finally
      {
        await CleanupYamlFileOnly(yamlPath);
      }
    }

    [Fact]
    public async Task Down_NonExistentFile_FailsGracefully()
    {
      var result = await KubernetesDriver.DownAsync(
          Context, "/tmp/nonexistent-file-" + Guid.NewGuid().ToString("N") + ".yaml", TestContext.Current.CancellationToken);

      Assert.False(result.Success);
    }

    #endregion

    #region Generate

    [Fact]
    public async Task GenerateKube_FromPod_ReturnsYaml()
    {
      string? podName = null;
      try
      {
        await EnsureImageAsync(TestImage);
        podName = UniqueName("kube-gen");
        var createResult = await PodDriver.CreatePodAsync(
            Context, new PodCreateConfig { Name = podName }, TestContext.Current.CancellationToken);
        Assert.True(createResult.Success, $"Pod create failed: {createResult.Error}");

        // Pod must have at least one non-infra container for kube generate
        var containerResult = await ContainerDriver.CreateAsync(Context,
            new ContainerCreateConfig
            {
              Image = TestImage,
              Command = new[] { "sleep", "300" },
              Pod = podName
            }, TestContext.Current.CancellationToken);
        Assert.True(containerResult.Success,
            $"Container create failed: {containerResult.Error}");

        var genResult = await KubernetesDriver.GenerateAsync(Context, podName, TestContext.Current.CancellationToken);
        Assert.True(genResult.Success, $"Generate failed: {genResult.Error}");
        Assert.NotNull(genResult.Data);
        Assert.Contains("apiVersion", genResult.Data);
        Assert.Contains("kind", genResult.Data);
      }
      finally
      {
        if (podName != null)
        {
          try
          { await RemovePodAsync(podName); }
          catch { }
        }
      }
    }

    [Fact]
    public async Task GenerateKube_NonExistentResource_FailsGracefully()
    {
      var result = await KubernetesDriver.GenerateAsync(
          Context, "nonexistent-" + Guid.NewGuid().ToString("N")[..12], TestContext.Current.CancellationToken);

      Assert.False(result.Success);
    }

    #endregion

    #region Helpers

    private static string CreateTempYaml(string yaml)
    {
      var path = Path.Combine(Path.GetTempPath(),
          $"fluentdocker-test-{Guid.NewGuid():N}.yaml");
      File.WriteAllText(path, yaml);
      return path;
    }

    /// <summary>
    /// Tears down K8s resources and deletes the temp YAML file.
    /// Each operation is wrapped to ensure cleanup proceeds on error.
    /// </summary>
    private async Task CleanupYaml(string yamlPath)
    {
      if (string.IsNullOrEmpty(yamlPath))
        return;

      try
      { await KubernetesDriver.DownAsync(Context, yamlPath); }
      catch { }
      try
      { File.Delete(yamlPath); }
      catch { }
    }

    /// <summary>
    /// Deletes the temp YAML file only (resources already cleaned up).
    /// </summary>
    private static Task CleanupYamlFileOnly(string yamlPath)
    {
      if (!string.IsNullOrEmpty(yamlPath))
      {
        try
        { File.Delete(yamlPath); }
        catch { }
      }
      return Task.CompletedTask;
    }

    #endregion
  }
}
