using System.Collections.Generic;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Executors.Parsers;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Stacks;

namespace Ductus.FluentDocker.Commands
{
    public interface IContextEndpoint
    {
        /// <summary>
        /// Copy the context (docker or kubernetes) from the following named context.
        /// </summary>
        /// <value>Name of the docker / kubernetes context.</value>
        string From { get; set; }
    }
    public sealed class DockerEndpoint : IContextEndpoint
    {
        /// <summary>
        /// Copy the context from a named docker context.
        /// </summary>
        /// <value>The name to copy the docker context config from</value>
        /// <remarks>
        /// If this set to null or empty string (default) it will use
        /// the current docker context.
        /// </remarks>
        public string From { get; set; }
        /// <summary>
        /// The URI to the remote host e.g. ssh://nisse@remotemachine.com
        /// </summary>
        /// <value>The URI to the remote host.</value>
        public DockerUri Host { get; set; }
        /// <summary>
        /// Paths to certificates if remote host is protected using TLS.
        /// </summary>
        /// <value></value>
        public CertificatePaths Certificates { get; set; }
        /// <summary>
        /// Set this to true to skip TLS verify.
        /// </summary>
        /// <value>True if remote TLS certificate shall not be verified.</value>
        public bool SkipTLSVerify { get; set; }

        public override string ToString()
        {
            string s = "";
            if (null != Host)
            {
                s = $"host={Host}";
            }
            if (!string.IsNullOrEmpty(From))
            {
                s.CommaAdd($"from={From}");
            }
            if (SkipTLSVerify)
            {
                s.CommaAdd($"skip-tls-verify=true");
            }
            if (null != Certificates?.CaCertificate)
            {
                s.CommaAdd($"ca={Certificates.CaCertificate}");
            }
            if (null != Certificates?.ClientCertificate)
            {
                s.CommaAdd($"cert={Certificates.ClientCertificate}");
            }
            if (null != Certificates?.ClientKey)
            {
                s.CommaAdd($"key={Certificates.ClientKey}");
            }
            return s;
        }
    }

    public sealed class KubernetesEndpoint : IContextEndpoint
    {
        /// <summary>
        /// Copy the context from a named kubernetes configuration.
        /// </summary>
        /// <value>The name to copy the kubernetes config from</value>
        public string From { get; set; }
        /// <summary>
        /// The kubernetes configuration file path. 
        /// </summary>
        /// <value>Path to the kubernetes configuration file</value>
        public string Config { get; set; }
        /// <summary>
        /// Overrides the context set in the kubernetes config file.
        /// </summary>
        /// <value></value>
        public string ContextOverride { get; set; }
        /// <summary>
        /// Overrides the namespace set in the kubernetes config file
        /// </summary>
        /// <value></value>
        public string NamespaceOverride { get; set; }

        public override string ToString()
        {
            string s = "";
            if (!string.IsNullOrEmpty(From))
            {
                s = $"from={From}";
            }
            if (!string.IsNullOrEmpty(Config))
            {
                s.CommaAdd($"config-file={Config}");
            }
            if (!string.IsNullOrEmpty(ContextOverride))
            {
                s.CommaAdd($"context-override={ContextOverride}");
            }
            if (!string.IsNullOrEmpty(NamespaceOverride))
            {
                s.CommaAdd($"namespace-override={NamespaceOverride}");
            }
            return s;
        }
    }
    public static class Context
    {
        /// <summary>
        /// Creates a new docker or kubernetes based context.
        /// </summary>
        /// <param name="ep">The endpoint to create</param>
        /// <param name="name">Name of this new context.</param>
        /// <param name="description">Optional description of the context.</param>
        /// <param name="from">Optional a name of a existing context to clone from.</param>
        /// <param name="orchestrator">Optional a orchestrator to use.</param>
        /// <returns>The output of the creation of the context.</returns>
        public static CommandResponse<IList<string>> CreateContext(this IContextEndpoint ep,
            string name,
            string description = null,
            string from = null,
            Orchestrator orchestrator = Orchestrator.None)
        {
            string opts = "";
            if (!string.IsNullOrWhiteSpace(description))
            {
                opts = $" --description \"{description}\"";
            }
            if (!string.IsNullOrWhiteSpace(from))
            {
                opts += $" --from {from}";
            }
            if (orchestrator != Orchestrator.None)
            {
                opts += $" --default-stack-orchestrator {orchestrator.ToString().ToLower()}";
            }

            if (ep is KubernetesEndpoint)
            {
                opts += " " + ep.ToString();
            }
            else if (ep is DockerEndpoint)
            {
                opts += " " + ep.ToString();
            }
            else
            {
                throw new FluentDockerException($"The IContextEndpoint is neither docker or kubernetes");
            }

            return
       new ProcessExecutor<StringListResponseParser, IList<string>>(
         "docker".ResolveBinary(),
         $"context create {name}{opts}").Execute();
        }
    }
}