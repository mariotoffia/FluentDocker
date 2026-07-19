using FluentDocker.Drivers.Docker.Cli.Components.Parsing;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  [Trait("Category", "Unit")]
  public class ModelJsonParserProductionReadinessTests
  {
    [Fact]
    public void TryParsePsTable_InsertColumnBeforeBackend_Fails()
    {
      const string table =
          "MODEL NAME  PID   BACKEND    MODE\n" +
          "smollm2     4821  llama.cpp  completion\n";

      Assert.False(ModelJsonParser.TryParsePsTable(table, out var running));
      Assert.Empty(running);
    }
  }
}
