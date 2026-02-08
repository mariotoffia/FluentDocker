using System;

namespace FluentDocker.Drivers.Docker.Api.Connection
{
    /// <summary>
    /// Configuration for connecting to the Docker Engine REST API.
    /// </summary>
    public class DockerApiConnectionConfig
    {
        /// <summary>
        /// Docker daemon host URI. Supported schemes: unix://, npipe://, tcp://, https://.
        /// Default: auto-detected based on platform.
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// Path to directory containing TLS client certificates (ca.pem, cert.pem, key.pem).
        /// </summary>
        public string CertificatePath { get; set; }

        /// <summary>
        /// Whether to verify TLS certificates. Default: true.
        /// </summary>
        public bool VerifyTls { get; set; } = true;

        /// <summary>
        /// Connection timeout. Default: 30 seconds.
        /// </summary>
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// HTTP request timeout. Default: 5 minutes (long for build/pull operations).
        /// </summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Docker Engine API version to use. Null means auto-negotiate via /_ping.
        /// </summary>
        public string ApiVersion { get; set; }
    }
}
