using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Contract (shape) tests for the hexagonal model driver ports. These lock the
  /// port surface — adapters (CLI/HTTP) and mocks implement them downstream.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelDriverPortsTests
  {
    private static MethodInfo Method(Type t, string name) =>
        t.GetMethod(name) ?? throw new InvalidOperationException($"{t.Name}.{name} missing");

    [Theory]
    [InlineData("PullAsync")]
    [InlineData("ListAsync")]
    [InlineData("InspectAsync")]
    [InlineData("RemoveAsync")]
    [InlineData("TagAsync")]
    [InlineData("PushAsync")]
    [InlineData("PackageAsync")]
    [InlineData("PurgeAllAsync")]
    [InlineData("DiskUsageAsync")]
    public void ManagementDriver_HasMethod_WithDriverContextFirst(string method)
    {
      var m = Method(typeof(IModelManagementDriver), method);
      Assert.Equal(typeof(DriverContext), m.GetParameters()[0].ParameterType);
      Assert.StartsWith("Task", m.ReturnType.Name);
    }

    [Fact]
    public void ManagementDriver_ReturnsCommandResponse()
    {
      var list = Method(typeof(IModelManagementDriver), "ListAsync").ReturnType;
      // Task<CommandResponse<IList<ModelInfo>>>
      var inner = list.GetGenericArguments()[0];
      Assert.Equal(typeof(CommandResponse<>), inner.GetGenericTypeDefinition());
    }

    [Theory]
    [InlineData("StatusAsync")]
    [InlineData("VersionAsync")]
    [InlineData("ListRunningAsync")]
    [InlineData("LoadAsync")]
    [InlineData("UnloadAsync")]
    [InlineData("ConfigureAsync")]
    [InlineData("LogsAsync")]
    [InlineData("InstallRunnerAsync")]
    public void RuntimeDriver_HasMethod(string method)
    {
      var m = Method(typeof(IModelRuntimeDriver), method);
      Assert.Equal(typeof(DriverContext), m.GetParameters()[0].ParameterType);
    }

    [Fact]
    public void RuntimeDriver_LogsReturnsAsyncEnumerable()
    {
      var ret = Method(typeof(IModelRuntimeDriver), "LogsAsync").ReturnType;
      Assert.Equal(typeof(IAsyncEnumerable<string>), ret);
    }

    [Theory]
    [InlineData("ChatCompletionAsync")]
    [InlineData("ChatCompletionStreamAsync")]
    [InlineData("CompletionAsync")]
    [InlineData("CompletionStreamAsync")]
    [InlineData("EmbeddingsAsync")]
    [InlineData("ListEngineModelsAsync")]
    public void InferenceDriver_HasMethod(string method)
    {
      var m = Method(typeof(IModelInferenceDriver), method);
      Assert.Equal(typeof(DriverContext), m.GetParameters()[0].ParameterType);
    }

    [Fact]
    public void InferenceDriver_StreamingReturnsAsyncEnumerableOfChunk()
    {
      Assert.Equal(typeof(IAsyncEnumerable<ChatCompletionChunk>),
          Method(typeof(IModelInferenceDriver), "ChatCompletionStreamAsync").ReturnType);
      Assert.Equal(typeof(IAsyncEnumerable<CompletionChunk>),
          Method(typeof(IModelInferenceDriver), "CompletionStreamAsync").ReturnType);
    }

    [Fact]
    public void ApiConnection_IsAsyncDisposable_WithExpectedMembers()
    {
      var t = typeof(IModelApiConnection);
      Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(t));
      Assert.NotNull(t.GetProperty("BaseAddress"));
      foreach (var name in new[] { "GetAsync", "PostAsync", "DeleteAsync", "PostStreamAsync", "PingAsync" })
        Assert.NotNull(t.GetMethod(name));
    }

    [Fact]
    public void Ports_LastParameterIsCancellationToken()
    {
      foreach (var port in new[] { typeof(IModelManagementDriver), typeof(IModelRuntimeDriver), typeof(IModelInferenceDriver) })
      {
        foreach (var m in port.GetMethods())
        {
          var ps = m.GetParameters();
          Assert.Equal(typeof(CancellationToken), ps[ps.Length - 1].ParameterType);
        }
      }
    }
  }
}
