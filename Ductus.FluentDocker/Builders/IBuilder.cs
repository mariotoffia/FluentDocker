using System.Collections.Generic;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Builders
{
  public interface IBuilder
  {
    /// <summary>
    ///   Gets a parent builder if any.
    /// </summary>
    Option<IBuilder> Parent { get; }

    /// <summary>
    ///   Gets the root builder (if hiearchy).
    /// </summary>
    Option<IBuilder> Root { get; }

    /// <summary>
    ///   Gets the Childrens of this builder.
    /// </summary>
    IReadOnlyCollection<IBuilder> Children { get; }

    /// <summary>
    ///   Creates a new child <see cref="IBuilder" /> and makes this as parent.
    /// </summary>
    /// <returns></returns>
    IBuilder Create();

    IService Build();
  }

  public interface IBuilder<out T> : IBuilder
  {
    /// <summary>
    ///   Builds using the configuration and returns the instance.
    /// </summary>
    /// <typeparam name="T">The type to build from this builder.</typeparam>
    /// <returns>A instance if successfull, or and exception is thrown if any errors occurs.</returns>
    /// <exception cref="FluentDockerException">If any errors occurs during buildtime.</exception>
    new T Build();
  }
}
