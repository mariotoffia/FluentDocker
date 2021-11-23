namespace Ductus.FluentDocker.Execution
{
     public enum CommandType
    {
        /// <summary>
        /// Unknown command type.
        /// </summary>
        Unknown = 0,
        /// <summary>
        /// Shell is a command that will use the host environment shell to
        /// execute the command.
        /// </summary>
        Shell = 1,
        /// <summary>
        /// DockerREST will use the Docker REST API to execute the command.
        /// </summary>
        DockerREST  = 2
    }

}