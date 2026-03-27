using System;
using System.Collections.Generic;
using System.Text;
using FluentDocker.Extensions;
using FluentDocker.Model.Builders;
using FluentDocker.Model.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Services;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Extensions
{
  [Trait("Category", "Unit")]
  public class ModelExtensionsTests
  {
    // ── SizeOptionIfValid ───────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SizeOptionIfValid_NullOrEmptyValue_ReturnsUnchanged(string? value)
    {
      var sb = new StringBuilder("cmd");
      var result = sb.SizeOptionIfValid("--size=", value!);
      Assert.Same(sb, result);
      Assert.Equal("cmd", sb.ToString());
    }

    [Fact]
    public void SizeOptionIfValid_ValidSize_AppendsOption()
    {
      var sb = new StringBuilder("cmd");
      sb.SizeOptionIfValid("--size=", "100m");
      Assert.Equal("cmd --size=100m", sb.ToString());
    }

    [Fact]
    public void SizeOptionIfValid_ExceedsMaxSize_ReturnsUnchanged()
    {
      var sb = new StringBuilder("cmd");
      // "2g" converts to 2 * 1024 * 1024 * 1024 = 2147483648
      sb.SizeOptionIfValid("--size=", "2g", maxSize: 1000);
      Assert.Equal("cmd", sb.ToString());
    }

    [Fact]
    public void SizeOptionIfValid_AtMaxSize_AppendsOption()
    {
      var sb = new StringBuilder("cmd");
      // "100b" converts to 100
      sb.SizeOptionIfValid("--size=", "100b", maxSize: 100);
      Assert.Equal("cmd --size=100b", sb.ToString());
    }

    [Fact]
    public void SizeOptionIfValid_InvalidFormat_ReturnsUnchanged()
    {
      var sb = new StringBuilder("cmd");
      sb.SizeOptionIfValid("--size=", "notanumber");
      Assert.Equal("cmd", sb.ToString());
    }

    // ── OptionIfExists(short?) ──────────────────────────────────────────

    [Fact]
    public void OptionIfExists_ShortHasValue_AppendsOption()
    {
      var sb = new StringBuilder("cmd");
      short? value = 42;
      sb.OptionIfExists("--port=", value);
      Assert.Equal("cmd --port=42", sb.ToString());
    }

    [Fact]
    public void OptionIfExists_ShortNull_ReturnsUnchanged()
    {
      var sb = new StringBuilder("cmd");
      short? value = null;
      sb.OptionIfExists("--port=", value);
      Assert.Equal("cmd", sb.ToString());
    }

    // ── OptionIfExists(string) ──────────────────────────────────────────

    [Fact]
    public void OptionIfExists_StringNonEmpty_AppendsOption()
    {
      var sb = new StringBuilder("cmd");
      sb.OptionIfExists("--name=", "mycontainer");
      Assert.Equal("cmd --name=mycontainer", sb.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void OptionIfExists_StringNullOrEmpty_ReturnsUnchanged(string? value)
    {
      var sb = new StringBuilder("cmd");
      sb.OptionIfExists("--name=", value!);
      Assert.Equal("cmd", sb.ToString());
    }

    // ── OptionIfExists(bool) ────────────────────────────────────────────

    [Fact]
    public void OptionIfExists_BoolTrue_AppendsOption()
    {
      var sb = new StringBuilder("cmd");
      sb.OptionIfExists("--detach", true);
      Assert.Equal("cmd --detach", sb.ToString());
    }

    [Fact]
    public void OptionIfExists_BoolFalse_ReturnsUnchanged()
    {
      var sb = new StringBuilder("cmd");
      sb.OptionIfExists("--detach", false);
      Assert.Equal("cmd", sb.ToString());
    }

    // ── OptionIfExists(string[]) ────────────────────────────────────────

    [Fact]
    public void OptionIfExists_StringArray_NonEmpty_AppendsAll()
    {
      var sb = new StringBuilder("cmd");
      sb.OptionIfExists("--env=", new[] { "A=1", "B=2" });
      Assert.Equal("cmd --env=A=1 --env=B=2", sb.ToString());
    }

    [Fact]
    public void OptionIfExists_StringArray_Null_ReturnsUnchanged()
    {
      var sb = new StringBuilder("cmd");
      sb.OptionIfExists("--env=", (string[])null!);
      Assert.Equal("cmd", sb.ToString());
    }

    [Fact]
    public void OptionIfExists_StringArray_Empty_ReturnsUnchanged()
    {
      var sb = new StringBuilder("cmd");
      sb.OptionIfExists("--env=", Array.Empty<string>());
      Assert.Equal("cmd", sb.ToString());
    }

    // ── OptionIfExists(IDictionary<string, string>) ─────────────────────

    [Fact]
    public void OptionIfExists_Dictionary_NonEmpty_AppendsKeyValuePairs()
    {
      var sb = new StringBuilder("cmd");
      var dict = new Dictionary<string, string>
      {
        { "key1", "val1" },
        { "key2", "val2" }
      };
      sb.OptionIfExists("--label=", dict);

      var result = sb.ToString();
      Assert.Contains("--label=key1=val1", result);
      Assert.Contains("--label=key2=val2", result);
    }

    [Fact]
    public void OptionIfExists_Dictionary_Null_ReturnsUnchanged()
    {
      var sb = new StringBuilder("cmd");
      sb.OptionIfExists("--label=", (IDictionary<string, string>)null!);
      Assert.Equal("cmd", sb.ToString());
    }

    [Fact]
    public void OptionIfExists_Dictionary_Empty_ReturnsUnchanged()
    {
      var sb = new StringBuilder("cmd");
      sb.OptionIfExists("--label=", new Dictionary<string, string>());
      Assert.Equal("cmd", sb.ToString());
    }

    // ── ToPlainId ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("sha256:abc123def", "abc123def")]
    [InlineData("abc123def", "abc123def")]
    [InlineData("", "")]
    public void ToPlainId_VariousInputs_ReturnsExpected(string input, string expected)
    {
      Assert.Equal(expected, input.ToPlainId());
    }

    [Fact]
    public void ToPlainId_MultipleColons_ReturnsOriginal()
    {
      // More than one colon means split.Length > 2, returns original
      var input = "sha256:abc:extra";
      Assert.Equal(input, input.ToPlainId());
    }

    // ── ToDocker(ContainerIsolationTechnology) ──────────────────────────

    [Theory]
    [InlineData(ContainerIsolationTechnology.Default, "default")]
    [InlineData(ContainerIsolationTechnology.Hyperv, "hyperv")]
    [InlineData(ContainerIsolationTechnology.Process, "process")]
    public void ToDocker_ContainerIsolation_ReturnsExpectedString(
      ContainerIsolationTechnology isolation, string expected)
    {
      Assert.Equal(expected, isolation.ToDocker());
    }

    [Fact]
    public void ToDocker_ContainerIsolation_Unknown_ReturnsNull()
    {
      Assert.Null(ContainerIsolationTechnology.Unknown.ToDocker());
    }

    // ── ToDocker(MountType) ─────────────────────────────────────────────

    [Theory]
    [InlineData(MountType.ReadOnly, "ro")]
    [InlineData(MountType.ReadWrite, "rw")]
    public void ToDocker_MountType_ReturnsExpectedString(MountType mount, string expected)
    {
      Assert.Equal(expected, mount.ToDocker());
    }

    [Fact]
    public void ToDocker_MountType_InvalidValue_ThrowsArgumentOutOfRangeException()
    {
      var invalid = (MountType)99;
      Assert.Throws<ArgumentOutOfRangeException>(() => invalid.ToDocker());
    }

    // ── AsTemplate ──────────────────────────────────────────────────────

    [Fact]
    public void AsTemplate_ConvertsStringToTemplateString()
    {
      TemplateString template = "hello-world".AsTemplate();
      Assert.NotNull(template);
      Assert.Equal("hello-world", template.ToString());
    }

    // ── ToServiceState ──────────────────────────────────────────────────

    [Fact]
    public void ToServiceState_NullState_ReturnsUnknown()
    {
      ContainerState state = null!;
      Assert.Equal(ServiceRunningState.Unknown, state.ToServiceState());
    }

    [Fact]
    public void ToServiceState_Dead_ReturnsStopped()
    {
      var state = new ContainerState { Dead = true };
      Assert.Equal(ServiceRunningState.Stopped, state.ToServiceState());
    }

    [Fact]
    public void ToServiceState_Restarting_ReturnsStarting()
    {
      var state = new ContainerState { Restarting = true };
      Assert.Equal(ServiceRunningState.Starting, state.ToServiceState());
    }

    [Fact]
    public void ToServiceState_Paused_ReturnsPaused()
    {
      var state = new ContainerState { Paused = true };
      Assert.Equal(ServiceRunningState.Paused, state.ToServiceState());
    }

    [Fact]
    public void ToServiceState_Running_ReturnsRunning()
    {
      var state = new ContainerState { Running = true };
      Assert.Equal(ServiceRunningState.Running, state.ToServiceState());
    }

    [Fact]
    public void ToServiceState_StatusCreated_ReturnsStopped()
    {
      var state = new ContainerState { Status = "created" };
      Assert.Equal(ServiceRunningState.Stopped, state.ToServiceState());
    }

    [Fact]
    public void ToServiceState_StatusExited_ReturnsStopped()
    {
      var state = new ContainerState { Status = "exited" };
      Assert.Equal(ServiceRunningState.Stopped, state.ToServiceState());
    }

    [Fact]
    public void ToServiceState_StatusExitedUpperCase_ReturnsStopped()
    {
      var state = new ContainerState { Status = "Exited" };
      Assert.Equal(ServiceRunningState.Stopped, state.ToServiceState());
    }

    [Fact]
    public void ToServiceState_UnknownStatus_ReturnsUnknown()
    {
      var state = new ContainerState { Status = "something-else" };
      Assert.Equal(ServiceRunningState.Unknown, state.ToServiceState());
    }

    [Fact]
    public void ToServiceState_AllFalseNoStatus_ReturnsUnknown()
    {
      var state = new ContainerState();
      Assert.Equal(ServiceRunningState.Unknown, state.ToServiceState());
    }

    [Fact]
    public void ToServiceState_DeadTakesPrecedenceOverRunning()
    {
      // Dead is checked before Running in the method
      var state = new ContainerState { Dead = true, Running = true };
      Assert.Equal(ServiceRunningState.Stopped, state.ToServiceState());
    }

    // ── ArrayAdd ────────────────────────────────────────────────────────

    [Fact]
    public void ArrayAdd_NullArray_ReturnsNewArrayFromValues()
    {
      string[] arr = null!;
      var result = arr.ArrayAdd("a", "b");
      Assert.Equal(new[] { "a", "b" }, result);
    }

    [Fact]
    public void ArrayAdd_ExistingArray_ConcatenatesValues()
    {
      var arr = new[] { "a", "b" };
      var result = arr.ArrayAdd("c", "d");
      Assert.Equal(new[] { "a", "b", "c", "d" }, result);
    }

    [Fact]
    public void ArrayAdd_NullValues_ReturnsSameArray()
    {
      var arr = new[] { "a" };
      var result = arr.ArrayAdd(null!);
      Assert.Same(arr, result);
    }

    [Fact]
    public void ArrayAdd_EmptyValues_ReturnsSameArray()
    {
      var arr = new[] { "a" };
      var result = arr.ArrayAdd();
      Assert.Same(arr, result);
    }

    // ── ArrayAddDistinct ────────────────────────────────────────────────

    [Fact]
    public void ArrayAddDistinct_RemovesDuplicates()
    {
      var arr = new[] { "a", "b" };
      var result = arr.ArrayAddDistinct("b", "c");
      Assert.Equal(new[] { "a", "b", "c" }, result);
    }

    [Fact]
    public void ArrayAddDistinct_NoDuplicates_ReturnsCombined()
    {
      var arr = new[] { "a" };
      var result = arr.ArrayAddDistinct("b", "c");
      Assert.Equal(new[] { "a", "b", "c" }, result);
    }

    [Fact]
    public void ArrayAddDistinct_NullArray_ReturnsDistinctValues()
    {
      string[] arr = null!;
      var result = arr.ArrayAddDistinct("a", "a", "b");
      Assert.Equal(new[] { "a", "b" }, result);
    }

    // ── Fluent chaining ─────────────────────────────────────────────────

    [Fact]
    public void OptionIfExists_FluentChaining_BuildsCorrectString()
    {
      var result = new StringBuilder("docker run")
        .OptionIfExists("--name=", "web")
        .OptionIfExists("--detach", true)
        .OptionIfExists("--port=", new[] { "80:80", "443:443" })
        .OptionIfExists("--memory=", (string)null!)
        .ToString();

      Assert.Equal("docker run --name=web --detach --port=80:80 --port=443:443", result);
    }
  }
}
