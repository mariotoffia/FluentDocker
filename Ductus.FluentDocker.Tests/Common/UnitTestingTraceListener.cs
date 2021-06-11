using System;
using System.Diagnostics;

namespace Ductus.FluentDocker.Tests.Common
{
  internal sealed class UnitTestingTraceListener : TraceListener
  {
    private static readonly Action<string> doNothing = (x) => { /* Do nothing */ };
    public Action<string> OnWrite { get; set; } = doNothing;
    public Action<string> OnWriteLine { get; set; } = doNothing;
    public override string Name { get; set; } = "Unit Testing Trace Listener";

    public override void Write(string message) => OnWrite(message);

    public override void WriteLine(string message) => OnWriteLine(message);
  }
}
