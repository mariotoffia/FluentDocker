namespace Ductus.FluentDocker.Execution
{

  public interface ICommand
  {
    /// <summary>
    /// CommandType exposes the type of command.
    /// </summary>
    CommandType Type { get; }
    /// <summary>
    /// CommandCategory exposes the category of command.
    /// </summary>
    /// <value></value>
    CommandCategory Category { get; }
  }
}

