using System;
using System.Collections.Generic;

namespace Ductus.FluentDocker.Model
{
  public interface IFeature : IDisposable
  {
    /// <summary>
    /// The globally id and version if the feature separated with a single slash '/'.
    /// </summary>
    /// <remarks>
    /// For example: 'git/2.1.4'.
    /// </remarks>
    string Id { get; }
    /// <summary>
    /// Initializes the feature.
    /// </summary>
    /// <param name="settings">Any settings that the feature needs to execute.</param>
    /// <remarks>
    /// Some features are stateless and some requires some form of a state. E.g. it may
    /// need credentials or urls to perform its work each time <see cref="Execute"/> is
    /// invoked. 
    /// </remarks>
    void Initialize(IDictionary<string, string> settings = null);
    /// <summary>
    /// Executes the feature's functionality.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    void Execute(params string[] arguments);
  }
}