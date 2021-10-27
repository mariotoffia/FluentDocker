namespace Ductus.FluentDocker.Execution
{
    public enum CommandCategory 
    {
        /// <summary>
        /// Unknown commnad category.
        /// </summary>
        Unknown = 0,
        /// <summary>
        /// Container is manageing a single container approach such as create container.
        /// </summary>
        /// <remarks>
        /// Even though it is possible to create multiple containers, when the command is in this
        /// category, it is assumed that the command is for a single container.
        /// </remarks>
        Container = 1,
        /// <summary>
        /// Composite is when e.g. using docker compose to allow for multiple container, network etc. operation.
        /// </summary>
        /// <remarks>
        /// Since e.g. docker compose may handle volumes, network etc. it is not a strict container command
        /// that is the yielded result.
        Composite = 2,
        /// <summary>
        /// Network is manageing docker networking.
        /// </summary>
        Network = 3,
        /// <summary>
        /// Volume is managing docker volumes.
        /// </summary>
        Volume = 4,
        /// <summary>
        /// Information command request.
        /// </summary>
        Info = 5,
        /// <summary>
        /// Machine is the legacy docker-machine command.
        /// </summary>
        Machine = 6,
        /// <summary>
        /// Stack manages docker stack operations.
        /// </summary>
        Stack  = 7
    }

}