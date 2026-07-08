using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public sealed class DockerApiChunk10HardeningItemsTests
  {
    private static DriverContext Ctx => new("docker-api-chunk10-hardening-test");

    [Fact]
    public async Task BuildAsync_OutputExactlyAtTailCap_HasNoTruncationMarker()
    {
      var dir = CreateScratchDirectory("build-exact-tail");
      try
      {
        await File.WriteAllTextAsync(Path.Combine(dir.FullName, "Dockerfile"),
            "FROM scratch\n", TestContext.Current.CancellationToken);
        var exactOutput = new string('x', CliOutputTruncation.DefaultTailChars);
        var mock = new MockDockerApiConnection();
        mock.SetupStream("/build",
            "{\"stream\":\"" + exactOutput + "\"}\n" +
            "{\"aux\":{\"ID\":\"sha256:deadbeef\"}}\n");
        var driver = new DockerApiImageDriver(mock);
        driver.Initialize(Ctx);

        var result = await driver.BuildAsync(Ctx,
            new ImageBuildConfig { BuildContext = dir.FullName }, null!,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var output = string.Join('\n', result.Data.Output);
        Assert.Equal(exactOutput, output);
        Assert.DoesNotContain(CliOutputTruncation.Marker(CliOutputTruncation.DefaultTailChars), output);
      }
      finally
      {
        dir.Delete(recursive: true);
      }
    }

    [Fact]
    public void TailBytes_AppendExactlyAtTailCap_HasNoTruncationMarker()
    {
      var type = typeof(DockerApiDriverBase).GetNestedType("TailBytes",
          BindingFlags.NonPublic)!;
      var tail = Activator.CreateInstance(type, 4)!;
      AppendBytes(type, tail, Encoding.UTF8.GetBytes("abcd"));
      var text = (string)type.GetMethod("ToText")!.Invoke(tail, null)!;

      Assert.Equal("abcd", text);
      Assert.DoesNotContain(CliOutputTruncation.Marker(CliOutputTruncation.DefaultTailChars), text);
    }

    private static void AppendBytes(Type tailType, object tail, byte[] bytes)
    {
      var method = tailType.GetMethod("Append")!;
      var spanCtor = typeof(ReadOnlySpan<byte>).GetConstructor([typeof(byte[])])!;
      var dm = new DynamicMethod("AppendTailBytes", null, [typeof(object), typeof(byte[])],
          typeof(DockerApiDriverBase).Module, skipVisibility: true);
      var il = dm.GetILGenerator();
      il.Emit(OpCodes.Ldarg_0);
      il.Emit(OpCodes.Castclass, tailType);
      il.Emit(OpCodes.Ldarg_1);
      il.Emit(OpCodes.Newobj, spanCtor);
      il.Emit(OpCodes.Callvirt, method);
      il.Emit(OpCodes.Ret);
      var append = (Action<object, byte[]>)dm.CreateDelegate(typeof(Action<object, byte[]>));
      append(tail, bytes);
    }

    private static DirectoryInfo CreateScratchDirectory(string name)
    {
      var path = Path.Combine(".out", "chunk10-hardening", name + "-" + Guid.NewGuid().ToString("N"));
      return Directory.CreateDirectory(path);
    }
  }
}
