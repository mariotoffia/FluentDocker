namespace Ductus.FluentDocker.Model.Containers
{
  public class Health
  {
    public HealthState Status { get; set; }
    public int FailingStreak { get; set; }
  }
}
