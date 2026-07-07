namespace FluentDocker.Services.Impl
{
  public partial class ComposeService
  {
    bool IServiceCapabilities.CanStart => true;
    bool IServiceCapabilities.CanStop => true;
    bool IServiceCapabilities.CanPause => true;
    bool IServiceCapabilities.CanRemove => true;
    bool IServiceCapabilities.CanHook => true;
  }
}
