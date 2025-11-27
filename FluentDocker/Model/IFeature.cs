using System;
using System.Collections.Generic;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Model
{
  public static class FeatureConstants
  {
    /// <summary>
    ///   Keeps the data and services when <see cref="IFeature" /> is <see cref="IDisposable.Dispose" />.
    /// </summary>
    public const string KeepOnDispose = "global.keep.on.dispose";

    /// <summary>
    ///   The <see cref="IHostService" /> to use in the <see cref="IFeature" /> when <see cref="IFeature.Initialize" /> is
    ///   invoked.
    /// </summary>
    /// <remarks>
    ///   Ownership is not transferred to the <see cref="IFeature" /> since this may be shared among several. If no
    ///   <see cref="IHostService" /> is passed to a <see cref="IFeature" /> and no special pattern for the specific feature
    ///   then the <see cref="IFeature" /> shall strive to use the native first, then the "default" after that the first
    ///   available it can get through docker machine.
    /// </remarks>
    public const string HostService = "globa.host.service";
  }

  public interface IFeature : IDisposable
  {
    /// <summary>
    ///   The globally id and version if the feature separated with a single slash '/'.
    /// </summary>
    /// <remarks>
    ///   For example: 'git/2.1.4'.
    /// </remarks>
    string Id { get; }

    /// <summary>
    ///   Currently bound services to the feature. It is never null.
    /// </summary>
    /// <remarks>
    ///   Those will not appear in this property until <see cref="Execute" /> has been invoked and will be removed
    ///   when <see cref="IDisposable.Dispose" /> has been invoked. The timeline between those two calls there may
    ///   be a set of services here. Note that it may be composite services and therefore a single instance represent
    ///   several services. Those are not flatten out in this property. Note that if any <see cref="IHostService" />
    ///   is used, it too will be exposed through this property.
    /// </remarks>
    IEnumerable<IService> Services { get; }

    /// <summary>
    ///   Initializes the feature.
    /// </summary>
    /// <param name="settings">Any settings that the feature needs to execute.</param>
    /// <remarks>
    ///   Some features are stateless and some requires some form of a state. E.g. it may
    ///   need credentials or urls to perform its work each time <see cref="Execute" /> is
    ///   invoked.
    /// </remarks>
    void Initialize(IDictionary<string, object> settings = null);

    /// <summary>
    ///   Executes the feature's functionality.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    void Execute(params string[] arguments);
  }
}
