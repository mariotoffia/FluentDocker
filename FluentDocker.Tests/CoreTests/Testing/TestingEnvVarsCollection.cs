using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  /// <summary>
  /// Serializes tests that mutate FluentDocker testing environment variables.
  /// </summary>
  [CollectionDefinition(Name, DisableParallelization = true)]
  public sealed class TestingEnvVarsCollection
  {
    public const string Name = "TestingEnvVars";
  }
}
