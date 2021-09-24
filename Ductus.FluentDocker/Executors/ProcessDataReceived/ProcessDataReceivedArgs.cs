using System;

namespace Ductus.FluentDocker.Executors.ProcessDataReceived
{
  public class ProcessDataReceivedArgs:EventArgs
  {
    public string ProcessIdentifier { get; set; }
    public string Data { get; set; }
  }
}
