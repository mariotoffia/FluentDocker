using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  public partial class ComposeDriverTests
  {
    #region CopyAsync Tests

    [Fact]
    public async Task Copy_FileToService_CopiesSuccessfully()
    {
      var projectName = UniqueName("compose");
      var composeFile = GetResourcePath("ComposeTests/RabbitMQ/docker-compose.yml");
      var tempFile = Path.GetTempFileName();

      try
      {
        await ComposeDriver.UpAsync(Context, new ComposeUpConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          Detached = true,
          RemoveOrphans = true
        }, TestContext.Current.CancellationToken);
        await Task.Delay(5000, TestContext.Current.CancellationToken);

        File.WriteAllText(tempFile, "test content from host");

        var copyResult = await ComposeDriver.CopyAsync(Context, new ComposeCopyConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          Source = tempFile,
          Destination = "rabbitmq:/tmp/testfile.txt"
        }, TestContext.Current.CancellationToken);

        Assert.True(copyResult.Success, $"Copy failed: {copyResult.Error}");

        // Verify file was copied via exec
        var execResult = await ComposeDriver.ExecuteAsync(Context,
            new ComposeExecConfig
            {
              ComposeFiles = [composeFile],
              ProjectName = projectName,
              Service = "rabbitmq",
              Command = ["cat", "/tmp/testfile.txt"],
              Tty = false
            }, TestContext.Current.CancellationToken);
        Assert.True(execResult.Success, $"Verify exec failed: {execResult.Error}");
        Assert.Contains("test content from host", execResult.Data);
      }
      finally
      {
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          RemoveVolumes = true
        }, TestContext.Current.CancellationToken);
        if (File.Exists(tempFile))
          File.Delete(tempFile);
      }
    }

    #endregion

  }
}
