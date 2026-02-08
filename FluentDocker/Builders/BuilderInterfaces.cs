using System.Collections.Generic;

namespace FluentDocker.Builders
{
    /// <summary>
    /// Network builder for lambda configuration.
    /// </summary>
    public interface INetworkBuilder
    {
        INetworkBuilder WithName(string name);
        INetworkBuilder UseDriver(string driver);
        INetworkBuilder WithSubnet(string subnet);
        INetworkBuilder WithGateway(string gateway);
        INetworkBuilder WithIPRange(string ipRange);
        INetworkBuilder WithIPv6(bool enableIPv6 = true);
        INetworkBuilder AsInternal(bool isInternal = true);
        INetworkBuilder WithLabel(string key, string value);
        INetworkBuilder WithOption(string key, string value);
        /// <summary>Remove network on dispose.</summary>
        INetworkBuilder RemoveOnDispose();
    }

    /// <summary>
    /// Volume builder for lambda configuration.
    /// </summary>
    public interface IVolumeBuilder
    {
        IVolumeBuilder WithName(string name);
        IVolumeBuilder UseDriver(string driver);
        IVolumeBuilder WithDriverOption(string key, string value);
        IVolumeBuilder WithLabel(string key, string value);
        /// <summary>Remove volume on dispose.</summary>
        IVolumeBuilder RemoveOnDispose();
    }

    /// <summary>
    /// Compose builder for lambda configuration.
    /// </summary>
    public interface IComposeBuilder
    {
        /// <summary>Add a compose file to use.</summary>
        IComposeBuilder WithComposeFile(string path);

        /// <summary>Add multiple compose files (for overrides/extensions).</summary>
        IComposeBuilder WithComposeFiles(params string[] paths);

        /// <summary>Set the project name.</summary>
        IComposeBuilder WithProjectName(string name);

        /// <summary>Set an environment variable for compose interpolation.</summary>
        IComposeBuilder WithEnvironment(string key, string value);

        /// <summary>Set multiple environment variables from a dictionary.</summary>
        IComposeBuilder WithEnvironment(IDictionary<string, string> environment);

        /// <summary>Load environment variables from an env file.</summary>
        IComposeBuilder WithEnvFile(string path);

        /// <summary>Build images before starting containers.</summary>
        IComposeBuilder WithBuild(bool build = true);

        /// <summary>Recreate containers even if configuration hasn't changed.</summary>
        IComposeBuilder WithForceRecreate(bool forceRecreate = true);

        /// <summary>Remove containers for services not defined in the compose file.</summary>
        IComposeBuilder WithRemoveOrphans(bool removeOrphans = true);

        /// <summary>Only operate on specific services.</summary>
        IComposeBuilder ForServices(params string[] services);

        /// <summary>Remove volumes on down.</summary>
        IComposeBuilder WithRemoveVolumes(bool removeVolumes = true);

        /// <summary>Remove images on down.</summary>
        IComposeBuilder WithRemoveImages(bool removeImages = true);

        /// <summary>Set a timeout for container shutdown (seconds).</summary>
        IComposeBuilder WithTimeout(int seconds);

        /// <summary>Scale a service to the specified number of replicas.</summary>
        IComposeBuilder WithScale(string service, int replicas);

        /// <summary>Don't start linked services.</summary>
        IComposeBuilder WithNoDeps(bool noDeps = true);

        /// <summary>Don't start the project (useful for testing config only).</summary>
        IComposeBuilder WithNoStart(bool noStart = true);

        /// <summary>Always pull images before running.</summary>
        IComposeBuilder WithPull(bool always = true);

        /// <summary>Wait for services to be healthy before considering them started.</summary>
        IComposeBuilder WithWait(bool wait = true);

        /// <summary>Set the wait timeout (seconds).</summary>
        IComposeBuilder WithWaitTimeout(int seconds);

        /// <summary>Use a custom profiles set.</summary>
        IComposeBuilder WithProfiles(params string[] profiles);
    }
}
