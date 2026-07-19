using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Non-parallel xUnit (v3) collection for tests that mutate process-wide environment
  /// variables consumed by the Docker Model Runner subsystem (e.g. <c>LLM_URL</c>,
  /// <c>LLM_MODEL</c>, <c>AI_MODEL_*</c>, <c>DOCKER_MODEL_RUNNER_URL</c>).
  /// <para>
  /// <see cref="Environment.SetEnvironmentVariable(string,string)"/> is process-global, so
  /// two test classes that set/read the SAME variable concurrently can observe each other's
  /// writes (a flaky, order-dependent race). By default in xUnit v3 each test class is its
  /// own collection and DIFFERENT collections run in parallel — which is exactly the racy
  /// case here. Placing every env-mutating model test class in this single collection with
  /// <see cref="CollectionDefinitionAttribute.DisableParallelization"/> forces them to run
  /// sequentially (relative to one another), eliminating the cross-class race while each test
  /// still restores/clears the variables it set via try/finally.
  /// </para>
  /// </summary>
  [CollectionDefinition(Name, DisableParallelization = true)]
  public sealed class ModelEnvVarsCollection
  {
    /// <summary>The collection name annotated on env-mutating model test classes.</summary>
    public const string Name = "ModelEnvVars";
  }
}
