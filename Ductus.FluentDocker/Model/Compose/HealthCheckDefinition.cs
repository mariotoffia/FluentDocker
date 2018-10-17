using System.Collections.Generic;

namespace Ductus.FluentDocker.Model.Compose
{
  /// <summary>
  ///   Configure a check that’s run to determine whether or not containers for this service are “healthy”.
  /// </summary>
  /// <remarks>
  ///   Version 2.1 file format and up. See the docs for the HEALTHCHECK Dockerfile instruction for details on how
  ///   healthchecks work.
  ///   healthcheck:
  ///   test: ["CMD", "curl", "-f", "http://localhost"]
  ///   interval: 1m30s
  ///   timeout: 10s
  ///   retries: 3
  ///   start_period: 40s
  ///   interval, timeout and start_period are specified as durations.
  ///   Note: start_period is only supported for v3.4 and higher of the compose file format.
  ///   test must be either a string or a list. If it’s a list, the first item must be either NONE, CMD or CMD-SHELL.
  ///   If it’s a string, it’s equivalent to specifying CMD-SHELL followed by that string.
  ///   # Hit the local web app
  ///   test: ["CMD", "curl", "-f", "http://localhost"]
  ///   # As above, but wrapped in /bin/sh. Both forms below are equivalent.
  ///   test: ["CMD-SHELL", "curl -f http://localhost || exit 1"]
  ///   test: curl -f https://localhost || exit 1
  ///   To disable any default healthcheck set by the image, you can use disable: true. This is equivalent to specifying
  ///   test: ["NONE"].
  ///   healthcheck:
  ///   disable: true
  /// </remarks>
  public sealed class HealthCheckDefinition
  {
    /// <summary>
    ///   If the health check is enabled or disabled.
    /// </summary>
    /// <remarks>
    ///   If set to false, the IMAGE health check is not used or this definition. If set to true but nothing is specified
    ///   here, the IMAGE health check will be used (if any). Otherwise if true and definition exist here it will use
    ///   this definition.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///   The test to perform when doing a health check.
    /// </summary>
    /// <remarks>
    ///   For example: ["CMD", "curl", "-f", "http://localhost"] or curl -f https://localhost || exit 1
    ///   The command after the CMD keyword can be either a shell command (e.g. HEALTHCHECK CMD /bin/check-running) or an
    ///   exec array (as with other Dockerfile commands; see e.g. ENTRYPOINT for details). The command’s exit status
    ///   indicates the health status of the container. The possible values are:
    ///   0: success - the container is healthy and ready for use
    ///   1: unhealthy - the container is not working correctly
    ///   2: reserved - do not use this exit code
    ///   For example, to check every five minutes or so that a web-server is able to serve the site’s main page within
    ///   three seconds:
    ///   HEALTHCHECK --interval=5m --timeout=3s \
    ///   CMD curl -f http://localhost/ || exit 1
    ///   To help debug failing probes, any output text (UTF-8 encoded) that the command writes on stdout or stderr
    ///   will be stored in the health status and can be queried with docker inspect. Such output should be kept short
    ///   (only the first 4096 bytes are stored currently).
    ///   When the health status of a container changes, a health_status event is generated with the new status.
    /// </remarks>
    public IList<string> Test { get; set; } = new List<string>();

    /// <summary>
    ///   How often the health check shall be executed.
    /// </summary>
    /// <remarks>
    ///   It is specified in duration e.g 1m30s. Default is 30s.
    /// </remarks>
    public string Interval { get; set; } = "30s";

    /// <summary>
    ///   How long to wait until it will fail the health check attempt when no answer is returned.
    /// </summary>
    /// <remarks>
    ///   It is specified in duration e.g. 10s. Default is 30s.
    /// </remarks>
    public string Timeout { get; set; } = "30s";

    /// <summary>
    ///   It takes retries consecutive failures of the health check for the container to be considered unhealthy.
    /// </summary>
    /// <remarks>
    ///   Default is 3.
    /// </remarks>
    public int Retries { get; set; } = 3;

    /// <summary>
    /// </summary>
    /// <remarks>
    ///   It is specified in duration e.g. 40s. start period provides initialization time for containers that need time
    ///   to bootstrap. Probe failure during that period will not be counted towards the maximum number of retries.
    ///   However, if a health check succeeds during the start period, the container is considered started and all
    ///   consecutive failures will be counted towards the maximum number of retries. Default is 0s.
    ///   Note: start_period is only supported for v3.4 and higher of the compose file format.
    /// </remarks>
    public string StartPeriod { get; set; } = "0s";
  }
}