using System.Collections.Generic;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Compose;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Builders
{
  /// <summary>
  /// Builds a docker-compose service.
  /// </summary>
  /// <remarks>
  /// This is then written to a docker-compose.yml compatible format and can be used
  /// to instantiate one or more services.
  /// </remarks>
  public class ComposeServiceBuilder
  {
    private readonly ComposeServiceDefinition _config = new ComposeServiceDefinition();

    internal ComposeServiceBuilder(string name)
    {
      _config.Name = name;
    }

    public ComposeServiceBuilder Image(string image)
    {
      _config.Image = image;
      return this;
    }

    public ComposeServiceBuilder Volume(string name, TemplateString containerPath)
    {
      _config.Volumes.Add($"{name}:{containerPath.Rendered.EscapePath()}");
      return this;
    }

    public ComposeServiceBuilder Restart(RestartPolicy policy)
    {
      _config.Restart = policy;
      return this;
    }

    /// <summary>
    /// Environment are expressed either as name=value or name, value.
    /// </summary>
    /// <param name="nameAndValue">The name=value format or every even is name and every odd is value.</param>
    /// <returns>Itself for fluent access.</returns>
    public ComposeServiceBuilder Environment(params string[] nameAndValue)
    {
      if (null == nameAndValue || 0 == nameAndValue.Length) return this;
      string name = null;
      foreach (var v in nameAndValue)
      {
        var idx = v.IndexOf('=');
        if (-1 == idx)
        {
          if (null == name)
          {
            name = v;
            continue;
          }

          _config.Environment.Add(name, v);
          name = null;
          continue;
        }

        if (null != name)
        {
          throw new FluentDockerException("Either specify name=value format or every even is name and every odd is value.");
        }
        
        _config.Environment.Add(v.Substring(0, idx), v.Substring(idx + 1));
      }

      return this;
    }

    public ComposeServiceBuilder DependsOn(params string[] services)
    {
      if (null == services || 0 == services.Length) return this;
      
      ((List<string>)_config.DependsOn).AddRange(services);
      return this;
    }

    public ComposeServiceBuilder Ports(params string[] ports)
    {
      if (null == ports || 0 == ports.Length) return this;

      ((List<string>)_config.Ports).AddRange(ports);
      return this;
    }
  }
}