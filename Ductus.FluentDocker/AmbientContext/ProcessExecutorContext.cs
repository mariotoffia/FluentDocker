using System;
using Ductus.FluentDocker.AmbientContex;
using Ductus.FluentDocker.Executors;

namespace Ductus.FluentDocker.AmbientContext
{
  public class ProcessManagerContext
  {
    private static readonly ThreadVariable<ProcessManager> ProcessManagerThreadVariable = new ThreadVariable<ProcessManager>(new ProcessManager());

    public static ProcessManager ProcessManager => ProcessManagerThreadVariable.Current;

    public static IDisposable UseProcessManager(ProcessManager processManager)
    {
      return ProcessManagerThreadVariable.Use(processManager);
    }
  }
}
