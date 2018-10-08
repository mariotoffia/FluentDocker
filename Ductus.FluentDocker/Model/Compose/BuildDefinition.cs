using System.Collections.Generic;

namespace Ductus.FluentDocker.Model.Compose
{
  public sealed class BuildDefinition
  {
    /// <summary>
    ///   Either a path to a directory containing a Dockerfile, or a url to a git repository.
    /// </summary>
    /// <remarks>
    ///   When the value supplied is a relative path, it is interpreted as relative to the location of the Compose file. This
    ///   directory is also the build context that is sent to the Docker daemon.
    ///   Compose builds and tags it with a generated name, and use that image thereafter.
    /// </remarks>
    public string Context { get; set; }

    /// <summary>
    ///   Alternate Dockerfile.
    /// </summary>
    /// <remarks>
    ///   Compose uses an alternate file to build with. A build path must also be specified.
    /// </remarks>
    public string Dockerfile { get; set; }

    /// <summary>
    ///   Add build arguments, which are environment variables accessible only during the build process.
    /// </summary>
    /// <remarks>
    ///   First, specify the arguments in your Dockerfile:
    ///   ARG buildno
    ///   ARG gitcommithash
    ///   RUN echo "Build number: $buildno"
    ///   RUN echo "Based on commit: $gitcommithash"
    ///   Then specify the arguments under the build key. You can pass a mapping or a list:
    ///   build:
    ///   context: .
    ///   args:
    ///   buildno: 1
    ///   gitcommithash: cdc3b19
    ///   build:
    ///     context: .
    ///     args:
    ///     - buildno=1
    ///     - gitcommithash=cdc3b19
    ///   You can omit the value when specifying a build argument, in which case its value at build time is the value in the
    ///   environment where Compose is running.
    ///   args:
    ///   - buildno
    ///   - gitcommithash
    /// </remarks>
    public IList<string> Args { get; set; } = new List<string>();
    /// <summary>
    /// A list of images that the engine uses for cache resolution.
    /// </summary>
    /// <remarks>
    /// Note: This option is new in v3.2.
    /// cache_from:
    ///   - alpine:latest
    ///   - corp/web_app:3.14
    /// </remarks>
    public IList<string> CacheFrom { get; set; } = new List<string>();
    /// <summary>
    /// Add metadata to the resulting image using Docker labels.
    /// </summary>
    /// <remarks>
    /// Note: This option is new in v3.3.
    /// labels:
    ///   com.example.description: "Accounting webapp"
    ///   com.example.department: "Finance"
    ///   com.example.label-with-empty-value: ""
    /// </remarks>
    public IDictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
    /// <summary>
    /// Set the size of the /dev/shm partition for this build’s containers.
    /// </summary>
    /// <remarks>
    /// Added in version 3.5 file format.
    /// Specify as an integer value representing the number of bytes or as a string expressing a byte value.
    /// For example:
    /// build:
    ///   shm_size: '2gb'
    /// or
    ///   shm_size: 1000000
    /// </remarks>
    public string ShmSize { get; set; }
    /// <summary>
    /// Build the specified stage as defined inside the Dockerfile.
    /// </summary>
    /// <remarks>
    /// Added in version 3.4 file format.
    /// build:
    ///   target: prod
    /// </remarks>
    public string Target { get; set; }
  }
}