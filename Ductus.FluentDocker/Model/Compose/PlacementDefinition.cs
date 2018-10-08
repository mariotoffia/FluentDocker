using System.Collections.Generic;

namespace Ductus.FluentDocker.Model.Compose
{
  /// <summary>
  /// Specify placement of constraints and preferences.
  /// </summary>
  public sealed class PlacementDefinition
  {
    /// <summary>
    /// Limit the set of nodes where a task can be scheduled by defining constraint expressions.
    /// </summary>
    /// <remarks>
    /// Multiple constraints find nodes that satisfy every expression (AND match). Constraints can match node or
    /// Docker Engine labels as follows:
    /// <list type="table">  
    /// <listheader>  
    /// <term>node attribute</term>  
    /// <term>matches</term>  
    /// <term>example</term>  
    /// </listheader>  
    /// <item>  
    /// <term>node.id</term>  
    /// <term>Node ID</term>  
    /// <term>node.id==2ivku8v2gvtg4</term>  
    /// </item>  
    /// <item>  
    /// <term>node.hostname</term>  
    /// <term>Node hostname</term>  
    /// <term>node.hostname!=node-2</term>  
    /// </item>  
    /// <item>  
    /// <term>node.role</term>  
    /// <term>Node role</term>  
    /// <term>node.role==manager</term>  
    /// </item>  
    /// <item>  
    /// <term>node.labels</term>  
    /// <term>user defined node labels</term>  
    /// <term>node.labels.security==high</term>  
    /// </item>  
    /// <item>  
    /// <term>engine.labels</term>  
    /// <term>Docker Engine's labels</term>  
    /// <term>engine.labels.operatingsystem==ubuntu 14.04</term>  
    /// </item>  
    /// </list>
    ///  Engine.labels apply to Docker Engine labels like operating system, drivers, etc. Swarm administrators add
    /// node.labels for operational purposes by using the docker node update command.
    /// </remarks>
    public IList<string> Constraints { get; set; } = new List<string>();
    /// <summary>
    /// Set up the service to divide tasks evenly over different categories of nodes. 
    /// </summary>
    /// <remarks>
    /// For example spread: node.labels.zone
    /// </remarks>
    public IDictionary<string, string> Preferences { get; set; } = new Dictionary<string, string>();
  }
}