using System.Collections.Generic;

namespace Ductus.FluentDocker.Model.Compose
{
  /// <summary>
  ///   Logging configuration for the service.
  /// </summary>
  /// <remarks>
  ///   For example:
  ///   logging:
  ///   driver: syslog
  ///   options:
  ///   syslog-address: "tcp://192.168.0.42:123"
  /// </remarks>
  public sealed class LoggingDefinition
  {
    /// <summary>
    ///   Specifies the driver to use for logging.
    /// </summary>
    /// <remarks>
    ///   The default value is json-file.
    ///   Valid drivers are: json-file, syslog, none
    ///   Note: Only the json-file and journald drivers make the logs available directly from docker-compose up and
    ///   docker-compose logs. Using any other driver does not print any logs.
    /// </remarks>
    public string Driver { get; set; } = "json-file";

    /// <summary>
    ///   Specify logging options for the logging driver.
    /// </summary>
    /// <remarks>
    ///   For example syslog option: syslog-address: "tcp://192.168.0.42:123".
    ///   Note: Logging options available depend on which logging driver you use.
    /// </remarks>
    public IDictionary<string, string> Options { get; set; } = new Dictionary<string, string>();
  }
}