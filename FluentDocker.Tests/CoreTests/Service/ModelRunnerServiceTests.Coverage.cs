using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using FluentDocker.Model.Models.Options;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Additional facade-coverage tests for <see cref="ModelRunnerService"/>: the
  /// previously-untested store/engine public methods, exercised for both
  /// delegation/projection (success) and <c>CommandResponse</c>→
  /// <c>ModelRunnerException</c> translation (failure). Inference-method coverage
  /// plus the cross-cutting fail-fast / disposed / option / cancellation tests live
  /// in the <c>.CoverageInference</c> partial. New additive Mock helpers are in
  /// <c>MockDriverPack.Model.Coverage.cs</c>.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class ModelRunnerServiceTests
  {
    // ======================== IModelStore =================================

    [Fact]
    public async Task Store_PullAsync_TranslatesData()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelPull(
          new ModelInfo { Reference = ModelReference.Parse("ai/smollm2"), Size = 4096 }));
      await using (kernel)
      {
        var info = await runner.PullAsync(ModelReference.Parse("ai/smollm2"), null, TestContext.Current.CancellationToken);
        Assert.Equal("smollm2", info.Reference.Name);
        Assert.Equal(4096, info.Size);
      }
    }

    [Fact]
    public async Task Store_PullAsync_Failure_ThrowsModelRunnerException()
    {
      var (kernel, runner) = await BuildAsync(p =>
          p.ModelManagementDriver.Setup(d => d.PullAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
                  It.IsAny<IProgress<ModelPullProgress>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(CommandResponse<ModelInfo>.Fail("boom", ErrorCodes.Model.PullFailed)));
      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<ModelRunnerException>(
            () => runner.PullAsync(ModelReference.Parse("ai/smollm2"), null, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.Model.PullFailed, ex.ErrorCode);
      }
    }

    [Fact]
    public async Task Store_InspectAsync_TranslatesData()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelInspect(
          new ModelInfo { Reference = ModelReference.Parse("ai/smollm2"), Architecture = "llama" }));
      await using (kernel)
      {
        var info = await runner.InspectAsync(ModelReference.Parse("ai/smollm2"), TestContext.Current.CancellationToken);
        Assert.Equal("llama", info.Architecture);
      }
    }

    [Fact]
    public async Task Store_InspectAsync_Failure_ThrowsModelRunnerException()
    {
      var (kernel, runner) = await BuildAsync(p =>
          p.ModelManagementDriver.Setup(d => d.InspectAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(CommandResponse<ModelInfo>.Fail("nope", ErrorCodes.Model.InspectFailed)));
      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<ModelRunnerException>(
            () => runner.InspectAsync(ModelReference.Parse("ai/smollm2"), TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.Model.InspectFailed, ex.ErrorCode);
      }
    }

    [Fact]
    public async Task Store_TagAsync_DelegatesToDriver()
    {
      var (kernel, runner, pack) = await BuildWithPackAsync(p => p.SetupModelTag());
      await using (kernel)
      {
        var src = ModelReference.Parse("ai/smollm2");
        var tgt = ModelReference.Parse("ai/smollm2:mine");
        await runner.TagAsync(src, tgt, TestContext.Current.CancellationToken);
        pack.ModelManagementDriver.Verify(
            d => d.TagAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<ModelReference>(), It.IsAny<CancellationToken>()),
            Times.Once);
      }
    }

    [Fact]
    public async Task Store_TagAsync_Failure_ThrowsModelRunnerException()
    {
      var (kernel, runner) = await BuildAsync(p =>
          p.ModelManagementDriver.Setup(d => d.TagAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
                  It.IsAny<ModelReference>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(CommandResponse<Unit>.Fail("bad tag", ErrorCodes.Model.TagFailed)));
      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<ModelRunnerException>(
            () => runner.TagAsync(ModelReference.Parse("ai/smollm2"), ModelReference.Parse("ai/smollm2:mine"),
                TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.Model.TagFailed, ex.ErrorCode);
      }
    }

    [Fact]
    public async Task Store_PushAsync_DelegatesToDriver()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelPush());
      await using (kernel)
      {
        await runner.PushAsync(ModelReference.Parse("ai/smollm2"), TestContext.Current.CancellationToken);
      }
    }

    [Fact]
    public async Task Store_PushAsync_Failure_ThrowsModelRunnerException()
    {
      var (kernel, runner) = await BuildAsync(p =>
          p.ModelManagementDriver.Setup(d => d.PushAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(CommandResponse<Unit>.Fail("push fail", ErrorCodes.Model.PushFailed)));
      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<ModelRunnerException>(
            () => runner.PushAsync(ModelReference.Parse("ai/smollm2"), TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.Model.PushFailed, ex.ErrorCode);
      }
    }

    [Fact]
    public async Task Store_PackageAsync_TranslatesData()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelPackage(
          new ModelInfo { Reference = ModelReference.Parse("ai/packaged"), Format = "gguf" }));
      await using (kernel)
      {
        var info = await runner.PackageAsync(
            new ModelPackageRequest { GgufPath = "/tmp/model.gguf", Target = ModelReference.Parse("ai/packaged") },
            TestContext.Current.CancellationToken);
        Assert.Equal("packaged", info.Reference.Name);
        Assert.Equal("gguf", info.Format);
      }
    }

    [Fact]
    public async Task Store_PackageAsync_Failure_ThrowsModelRunnerException()
    {
      var (kernel, runner) = await BuildAsync(p =>
          p.ModelManagementDriver.Setup(d => d.PackageAsync(It.IsAny<DriverContext>(), It.IsAny<ModelPackageRequest>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(CommandResponse<ModelInfo>.Fail("pkg fail", ErrorCodes.Model.PackageFailed)));
      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<ModelRunnerException>(
            () => runner.PackageAsync(new ModelPackageRequest { GgufPath = "/tmp/m.gguf" }, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.Model.PackageFailed, ex.ErrorCode);
      }
    }

    [Fact]
    public async Task Store_PurgeAllAsync_TranslatesData()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelPurgeAll(
          new ModelPruneResult { Removed = new[] { "ai/a", "ai/b" }, ReclaimedBytes = 1234 }));
      await using (kernel)
      {
        var result = await runner.PurgeAllAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Removed.Count);
        Assert.Equal(1234, result.ReclaimedBytes);
      }
    }

    [Fact]
    public async Task Store_PurgeAllAsync_Failure_ThrowsModelRunnerException()
    {
      var (kernel, runner) = await BuildAsync(p =>
          p.ModelManagementDriver.Setup(d => d.PurgeAllAsync(It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(CommandResponse<ModelPruneResult>.Fail("prune fail", ErrorCodes.Model.PruneFailed)));
      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<ModelRunnerException>(() => runner.PurgeAllAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.Model.PruneFailed, ex.ErrorCode);
      }
    }

    [Fact]
    public async Task Store_DiskUsageAsync_TranslatesData()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelDiskUsage(
          new ModelDiskUsage { ModelCount = 3, ModelsSizeBytes = 9000, ReclaimableBytes = 100 }));
      await using (kernel)
      {
        var usage = await runner.DiskUsageAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, usage.ModelCount);
        Assert.Equal(9000, usage.ModelsSizeBytes);
      }
    }

    [Fact]
    public async Task Store_DiskUsageAsync_Failure_ThrowsModelRunnerException()
    {
      var (kernel, runner) = await BuildAsync(p =>
          p.ModelManagementDriver.Setup(d => d.DiskUsageAsync(It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(CommandResponse<ModelDiskUsage>.Fail("df fail", ErrorCodes.Model.DiskUsageFailed)));
      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<ModelRunnerException>(() => runner.DiskUsageAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.Model.DiskUsageFailed, ex.ErrorCode);
      }
    }

    // ======================== IModelEngine ================================

    [Fact]
    public async Task Engine_VersionAsync_TranslatesData()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelVersion(
          new ModelRunnerVersion { CliVersion = "v1.2.1", EngineVersion = "b1234", ApiVersion = "1.0" }));
      await using (kernel)
      {
        var version = await runner.VersionAsync(TestContext.Current.CancellationToken);
        Assert.Equal("v1.2.1", version.CliVersion);
        Assert.Equal("b1234", version.EngineVersion);
      }
    }

    [Fact]
    public async Task Engine_VersionAsync_Failure_ThrowsModelRunnerException()
    {
      var (kernel, runner) = await BuildAsync(p =>
          p.ModelRuntimeDriver.Setup(d => d.VersionAsync(It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(CommandResponse<ModelRunnerVersion>.Fail("ver fail", ErrorCodes.Model.VersionFailed)));
      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<ModelRunnerException>(() => runner.VersionAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.Model.VersionFailed, ex.ErrorCode);
      }
    }

    [Fact]
    public async Task Engine_ListRunningAsync_TranslatesData()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelRunning(
          new RunningModel { Reference = ModelReference.Parse("ai/smollm2"), Backend = "llama.cpp", Mode = "loaded" }));
      await using (kernel)
      {
        var running = await runner.ListRunningAsync(TestContext.Current.CancellationToken);
        Assert.Single(running);
        Assert.Equal("loaded", running[0].Mode);
        Assert.Equal("smollm2", running[0].Reference.Name);
      }
    }

    [Fact]
    public async Task Engine_ListRunningAsync_Failure_ThrowsModelRunnerException()
    {
      var (kernel, runner) = await BuildAsync(p =>
          p.ModelRuntimeDriver.Setup(d => d.ListRunningAsync(It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(CommandResponse<IList<RunningModel>>.Fail("ps fail", ErrorCodes.Model.ListFailed)));
      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<ModelRunnerException>(() => runner.ListRunningAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.Model.ListFailed, ex.ErrorCode);
      }
    }

    [Fact]
    public async Task Engine_UnloadAsync_DelegatesToDriver()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelUnload());
      await using (kernel)
      {
        await runner.UnloadAsync(ModelReference.Parse("ai/smollm2"), all: false, TestContext.Current.CancellationToken);
      }
    }

    [Fact]
    public async Task Engine_UnloadAsync_AllTrue_PassesAllFlagToDriver()
    {
      var (kernel, runner, pack) = await BuildWithPackAsync(p => p.SetupModelUnload());
      await using (kernel)
      {
        await runner.UnloadAsync(null, all: true, TestContext.Current.CancellationToken);
        pack.ModelRuntimeDriver.Verify(
            d => d.UnloadAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), true, It.IsAny<CancellationToken>()),
            Times.Once);
      }
    }

    [Fact]
    public async Task Engine_UnloadAsync_Failure_ThrowsModelRunnerException()
    {
      var (kernel, runner) = await BuildAsync(p =>
          p.ModelRuntimeDriver.Setup(d => d.UnloadAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(CommandResponse<Unit>.Fail("unload fail", ErrorCodes.Model.UnloadFailed)));
      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<ModelRunnerException>(
            () => runner.UnloadAsync(ModelReference.Parse("ai/smollm2"), all: false, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.Model.UnloadFailed, ex.ErrorCode);
      }
    }

    [Fact]
    public async Task Engine_ConfigureAsync_DelegatesAndPropagatesOptions()
    {
      ModelConfigureOptions captured = null;
      var (kernel, runner) = await BuildAsync(p =>
          p.ModelRuntimeDriver.Setup(d => d.ConfigureAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
                  It.IsAny<ModelConfigureOptions>(), It.IsAny<CancellationToken>()))
              .Callback<DriverContext, ModelReference, ModelConfigureOptions, CancellationToken>((_, _, o, _) => captured = o)
              .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default)));
      await using (kernel)
      {
        await runner.ConfigureAsync(ModelReference.Parse("ai/smollm2"),
            new ModelConfigureOptions { ContextSize = 8192 }, TestContext.Current.CancellationToken);
        Assert.NotNull(captured);
        Assert.Equal(8192, captured.ContextSize);
      }
    }

    [Fact]
    public async Task Engine_ConfigureAsync_Failure_ThrowsModelRunnerException()
    {
      var (kernel, runner) = await BuildAsync(p =>
          p.ModelRuntimeDriver.Setup(d => d.ConfigureAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
                  It.IsAny<ModelConfigureOptions>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(CommandResponse<Unit>.Fail("cfg fail", ErrorCodes.Model.ConfigureFailed)));
      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<ModelRunnerException>(
            () => runner.ConfigureAsync(ModelReference.Parse("ai/smollm2"), new ModelConfigureOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.Model.ConfigureFailed, ex.ErrorCode);
      }
    }

    [Fact]
    public async Task Engine_LogsAsync_ProjectsLines()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelLogs("line-1", "line-2"));
      await using (kernel)
      {
        var collected = new List<string>();
        await foreach (var line in runner.LogsAsync(follow: false, TestContext.Current.CancellationToken))
          collected.Add(line);

        Assert.Equal(new[] { "line-1", "line-2" }, collected);
      }
    }

    [Fact]
    public async Task Engine_InstallRunnerAsync_DelegatesAndPropagatesOptions()
    {
      ModelRunnerInstallOptions captured = null;
      var (kernel, runner) = await BuildAsync(p =>
          p.ModelRuntimeDriver.Setup(d => d.InstallRunnerAsync(It.IsAny<DriverContext>(),
                  It.IsAny<ModelRunnerInstallOptions>(), It.IsAny<CancellationToken>()))
              .Callback<DriverContext, ModelRunnerInstallOptions, CancellationToken>((_, o, _) => captured = o)
              .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default)));
      await using (kernel)
      {
        await runner.InstallRunnerAsync(new ModelRunnerInstallOptions { Gpu = "cuda" }, TestContext.Current.CancellationToken);
        Assert.NotNull(captured);
        Assert.Equal("cuda", captured.Gpu);
      }
    }

    [Fact]
    public async Task Engine_InstallRunnerAsync_Failure_ThrowsModelRunnerException()
    {
      var (kernel, runner) = await BuildAsync(p =>
          p.ModelRuntimeDriver.Setup(d => d.InstallRunnerAsync(It.IsAny<DriverContext>(),
                  It.IsAny<ModelRunnerInstallOptions>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(CommandResponse<Unit>.Fail("install fail", ErrorCodes.Model.InstallFailed)));
      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<ModelRunnerException>(
            () => runner.InstallRunnerAsync(new ModelRunnerInstallOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.Model.InstallFailed, ex.ErrorCode);
      }
    }
  }
}
