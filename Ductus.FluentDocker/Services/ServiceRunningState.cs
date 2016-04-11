namespace Ductus.FluentDocker.Services
{
  public enum ServiceRunningState
  {
    Unknown = 0,
    Starting = 1,
    Running = 2,
    Paused = 3,
    Stopping = 4,
    Stopped = 5,
    Removed = 6
  }
}
