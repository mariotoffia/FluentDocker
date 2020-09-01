using System;
using Ductus.FluentDocker.AmbientContex;
using Ductus.FluentDocker.Executors.ProcessDataReceived;

namespace Ductus.FluentDocker.AmbientContext
{
  public class DataReceivedContext
  {
    private static readonly ThreadVariable<DataReceived> DataReceivedThreadVariable = new ThreadVariable<DataReceived>(new DataReceived());
    public static DataReceived DataReceived => DataReceivedThreadVariable.Current;

    public static IDisposable UseProcessManager(DataReceived processManager)
    {
      return DataReceivedThreadVariable.Use(processManager);
    }
  }
}
