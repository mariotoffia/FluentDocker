using System;

namespace Ductus.FluentDocker.Executors
{
  public interface IStreamMapper<T> where T : class
  {
    /// <summary>
    /// Invoked by the <see cref="StreamProcessExecutor{T,TE}"/> each time it gets new data.
    /// </summary>
    /// <param name="data">The data from the stdin or stderr from the spawned process.</param>
    /// <param name="isStdErr">It is set to false when stdin, otherwise it is stderr.</param>
    /// <returns>When data satisifies the type T it will be returned, otherwise null is returned.</returns>
    T OnData(string data, bool isStdErr);

    T OnProcessEnd(int exitCode);

    string Error { get; }
  }
}
