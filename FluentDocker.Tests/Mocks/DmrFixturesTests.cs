using Xunit;

namespace FluentDocker.Tests.Mocks
{
  /// <summary>
  /// Self-test for the <see cref="DmrFixtures"/> embedded-resource loader (M3).
  /// </summary>
  [Trait("Category", "Unit")]
  public class DmrFixturesTests
  {
    [Theory]
    [InlineData("ls.json")]
    [InlineData("inspect.json")]
    [InlineData("chat.json")]
    [InlineData("completion.json")]
    [InlineData("embeddings.json")]
    [InlineData("ps.txt")]
    [InlineData("df.txt")]
    [InlineData("ls.txt")]
    public void Load_ReturnsNonEmptyFixture(string name)
    {
      var content = DmrFixtures.Load(name);
      Assert.False(string.IsNullOrWhiteSpace(content));
    }

    [Fact]
    public void Load_Sse_ContainsDataLinesAndDoneTerminator()
    {
      var sse = DmrFixtures.Load("chat.sse");
      Assert.Contains("data:", sse);
      Assert.Contains("[DONE]", sse);
    }

    [Fact]
    public void LoadBytes_ReturnsBytes()
    {
      Assert.NotEmpty(DmrFixtures.LoadBytes("chat.json"));
    }

    [Fact]
    public void Load_MissingFixture_Throws()
    {
      Assert.ThrowsAny<System.Exception>(() => DmrFixtures.Load("does-not-exist.json"));
    }
  }
}
