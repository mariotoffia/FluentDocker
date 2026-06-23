namespace FluentDocker.Drivers
{
  /// <summary>
  /// Optional aggregate of the three model ports, for adapters/tests that
  /// implement everything. NOT required: per the ISP design, a CLI pack may
  /// register only management + runtime, and an HTTP pack only inference — no
  /// adapter is forced to stub a method it cannot honor.
  /// </summary>
  public interface IModelDriver
      : IModelManagementDriver, IModelRuntimeDriver, IModelInferenceDriver
  {
  }
}
