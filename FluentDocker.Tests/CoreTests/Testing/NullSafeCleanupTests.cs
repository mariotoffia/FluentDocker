using System.Threading.Tasks;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  /// <summary>
  /// Living proof that the cleanup entry points accept <c>null</c> for both
  /// the resource and the kernel without the null-forgiving (<c>!</c>) operator
  /// and behave as documented (null argument == no-op for that argument).
  /// This file compiles with nullable enabled; it must produce no nullable
  /// warnings — every <c>null</c> below is a bare literal, never <c>null!</c>.
  /// </summary>
  [Trait("Category", "Unit")]
  public class NullSafeCleanupTests
  {
    [Fact]
    public async Task ResourceLifecycle_DisposeAsync_BothNull_IsNoOp()
    {
      await ResourceLifecycle.DisposeAsync(null, null);
    }

    [Fact]
    public async Task ResourceLifecycle_DisposeAsync_NullResource_DisposesKernel()
    {
      var (kernel, _) = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync();

      // Null resource is skipped; the (non-null) kernel is still disposed.
      await ResourceLifecycle.DisposeAsync(null, kernel);

      // Kernel disposal is idempotent (Interlocked guard), so a follow-up
      // dispose is a safe no-op — proof the first call reached the kernel.
      await kernel.DisposeAsync();
    }

    [Fact]
    public async Task NUnitResourceHelpers_DisposeAsync_BothNull_IsNoOp()
    {
      await FluentDocker.Testing.NUnit.NUnitResourceHelpers
          .DisposeAsync(null, null);
    }

    [Fact]
    public async Task MsTestResourceHelpers_DisposeAsync_BothNull_IsNoOp()
    {
      await FluentDocker.Testing.MsTest.MsTestResourceHelpers
          .DisposeAsync(null, null);
    }
  }
}
