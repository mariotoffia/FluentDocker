using FluentDocker.Builders;
using FluentDocker.Kernel;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Tests for Wait Condition builder methods.
  /// </summary>
  [Trait("Category", "Unit")]
  public class WaitConditionTests
  {
    [Theory]
    [InlineData("5432/tcp")]
    [InlineData("5432")]
    [InlineData("80/tcp")]
    public void WaitForPort_SetsWaitCondition(string port)
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseImage("test")
                   .WaitForPort(port, 30000);
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void WaitForPort_WithAddress_SetsWaitCondition()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseImage("test")
                   .WaitForPort("5432/tcp", "localhost", 30000);
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void WaitForProcess_SetsWaitCondition()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseImage("test")
                   .WaitForProcess("postgres", 30000);
            configured = true;
          });
      Assert.True(configured);
    }

    [Theory]
    [InlineData("8080/tcp", "/")]
    [InlineData("8080/tcp", "/health")]
    [InlineData("8080/tcp", "/api/status")]
    public void WaitForHttp_SetsWaitCondition(string port, string path)
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseImage("test")
                   .WaitForHttp(port, path, 30000);
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void WaitForHttp_AdvancedOptions_SetsWaitCondition()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseImage("test")
                   .WaitForHttp(
                       "http://localhost:8080/health",
                       30000,
                       System.Net.Http.HttpMethod.Post,
                       "application/json",
                       "{\"test\":true}",
                       (response, iteration) => response.Code == System.Net.HttpStatusCode.OK ? -1 : 1000);
            configured = true;
          });
      Assert.True(configured);
    }

    [Theory]
    [InlineData("ready")]
    [InlineData("Server started")]
    [InlineData("database system is ready to accept connections")]
    public void WaitForLogMessage_SetsWaitCondition(string message)
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseImage("test")
                   .WaitForLogMessage(message, 30000);
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void WaitForHealthy_SetsWaitCondition()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseImage("test")
                   .WaitForHealthy(60000);
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void Wait_CustomLambda_SetsWaitCondition()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseImage("test")
                .Wait((service, iteration) =>
                {
                  // Return -1 to succeed, >0 to wait that many ms, 0 to continue immediately
                  if (iteration >= 5)
                    return -1;
                  return 1000;
                });
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void MultipleWaitConditions_CanBeChained()
    {
      var builder = new Builder();
      var configured = false;
      builder.WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseImage("test")
                   .WaitForPort("5432/tcp")
                   .WaitForLogMessage("ready")
                   .WaitForProcess("postgres");
            configured = true;
          });
      Assert.True(configured);
    }

    [Fact]
    public void WithWaitPollInterval_SetsIntervalOnSubsequentConditions()
    {
      // Use reflection to access internal _waitConditions list
      var containerBuilderType = typeof(Builder).Assembly
          .GetTypes().First(t => t.Name == "ContainerBuilder");
      var waitField = containerBuilderType
          .GetField("_waitConditions",
              System.Reflection.BindingFlags.NonPublic |
              System.Reflection.BindingFlags.Instance);
      var conditionType = typeof(Builder).Assembly
          .GetTypes().First(t => t.Name == "WaitCondition");
      var pollProp = conditionType.GetProperty("PollIntervalMs");

      IContainerBuilder captured = null;
      new Builder()
          .WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseImage("test")
                .WithWaitPollInterval(200)
                .WaitForPort("5432/tcp")
                .WaitForHealthy(5000);
            captured = c;
          });

      Assert.NotNull(captured);
      var conditions = waitField.GetValue(captured) as System.Collections.IList;
      Assert.Equal(2, conditions.Count);

      // Both conditions should have the custom poll interval
      Assert.Equal(200, (int)pollProp.GetValue(conditions[0]));
      Assert.Equal(200, (int)pollProp.GetValue(conditions[1]));
    }

    [Fact]
    public void WaitCondition_DefaultPollIntervalMs_Is500()
    {
      var containerBuilderType = typeof(Builder).Assembly
          .GetTypes().First(t => t.Name == "ContainerBuilder");
      var waitField = containerBuilderType
          .GetField("_waitConditions",
              System.Reflection.BindingFlags.NonPublic |
              System.Reflection.BindingFlags.Instance);
      var conditionType = typeof(Builder).Assembly
          .GetTypes().First(t => t.Name == "WaitCondition");
      var pollProp = conditionType.GetProperty("PollIntervalMs");

      IContainerBuilder captured = null;
      new Builder()
          .WithinDriver("test", new FluentDockerKernel())
          .UseContainer(c =>
          {
            c.UseImage("test").WaitForLogMessage("ready");
            captured = c;
          });

      var conditions = waitField.GetValue(captured) as System.Collections.IList;
      Assert.Single(conditions);
      Assert.Equal(500, (int)pollProp.GetValue(conditions[0]));
    }
  }
}

