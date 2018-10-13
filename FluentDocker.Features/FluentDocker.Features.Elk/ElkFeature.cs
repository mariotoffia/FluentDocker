using System;
using System.Collections.Generic;
using Ductus.FluentDocker.Model;

namespace FluentDocker.Features.Elk
{
  // https://github.com/libgit2/libgit2sharp/tree/master/LibGit2Sharp.Tests
  public class ElkFeature : IFeature
  {
    public void Dispose()
    {
      throw new NotImplementedException();
    }

    public string Id { get; }
    public void Initialize(IDictionary<string, string> settings = null)
    {
      throw new NotImplementedException();
    }

    public void Execute(params string[] arguments)
    {
      throw new NotImplementedException();
    }
  }
}