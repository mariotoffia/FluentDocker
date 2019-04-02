namespace Ductus.FluentDocker.Model.Stacks
{
  public sealed class StackLsResponse
  {
   public string Name { get; set; }
   public int Services { get; set; }
   public Orchestrator Orchestrator { get; set; }
   public string Namespace { get; set; }

   public static Orchestrator ToOrchestrator(string value)
   {
     if (string.IsNullOrEmpty(value)) return Orchestrator.All;
     
     value = value.ToLower();
     
     if (value.Equals("kubernetes")) return Orchestrator.Kubernetes;
     return value.Equals("swarm") ? Orchestrator.Swarm : Orchestrator.All;
   }
  }
}