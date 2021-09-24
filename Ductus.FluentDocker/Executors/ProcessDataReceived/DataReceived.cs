namespace Ductus.FluentDocker.Executors.ProcessDataReceived
{
  public class DataReceived
  {
    public delegate void OutputDataReceivedEventHandler(object sender, ProcessDataReceivedArgs ars);
    public OutputDataReceivedEventHandler OutputDataReceived;

    public delegate void ErrorDataReceivedEventHandler(object sender, ProcessDataReceivedArgs ars);
    public ErrorDataReceivedEventHandler ErrorDataReceived;

  }
}
