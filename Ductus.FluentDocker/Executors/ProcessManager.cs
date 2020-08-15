using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ductus.FluentDocker.Executors
{

  public delegate void ProcessManagerEventHandler<in TEventArgs>(ProcessManager sender, TEventArgs e);
  public delegate void ProcessManagerEventHandler(ProcessManager sender, EventArgs e);
  public class ProcessManager
  {
    private readonly Process _process = new Process();
    private readonly object _pendingWriteLock = new object();
    private SynchronizationContext _context;
    private string _pendingWriteData;
    internal int ExitCode => _process.ExitCode;

    public string ProcessIdentifier { get; private set; }
    public bool Running { get; private set; }
    public event ProcessManagerEventHandler<string> ErrorTextReceived;
    public event EventHandler ProcessExited;
    public event ProcessManagerEventHandler<string> StandartTextReceived;

    internal void ExecuteAsync(
      string processIdentifier,
      string fileName,
      string workingDirectory, IDictionary<string, string> environment = null,
      params string[] args)
    {
      if (Running)
      {
        throw new InvalidOperationException(
          "Process is still Running. Please wait for the process to complete.");
      }

      ProcessIdentifier = processIdentifier;
      _process.StartInfo.RedirectStandardError = true;
      _process.StartInfo.StandardErrorEncoding = Encoding.UTF8;

      _process.StartInfo.RedirectStandardInput = true;
      _process.StartInfo.RedirectStandardOutput = true;
      _process.EnableRaisingEvents = true;
      _process.StartInfo.CreateNoWindow = true;

      _process.StartInfo.UseShellExecute = false;

      _process.StartInfo.StandardOutputEncoding = Encoding.UTF8;

      _process.Exited += (sender, args) =>
      {
        Running = false;
        this.ProcessExited?.Invoke(sender, args);
      };


      if (environment != null && 0 != environment.Count)
      {
        foreach (var key in environment.Keys)
        {
#if COREFX
          _process.StartInfo.Environment[key] = environment[key];
#else
          _process.StartInfo.EnvironmentVariables[key] = environment[key];
#endif
        }
      }

      var arguments = string.Join(" ", args);

      _process.StartInfo.FileName = fileName;
      _process.StartInfo.Arguments = arguments;
      _process.StartInfo.WorkingDirectory = workingDirectory;

      _context = SynchronizationContext.Current;

      _process.Start();
      Running = true;

      new Task(WriteInputTask).Start();
      new Task(() => ListenToStream(_process.StandardOutput, OnStandartTextReceived)).Start();
      new Task(() => ListenToStream(_process.StandardError, OnErrorTextReceived)).Start();
    }

    internal void WaitForExit()
    {
      while (!_process.HasExited)
      {
        Thread.Sleep(1000);
      }
    }

    public void Write(string data)
    {
      if (data == null)
      {
        return;
      }

      lock (_pendingWriteLock)
      {
        _pendingWriteData = data;
      }
    }

    public void WriteLine(string data)
    {
      Write(data + Environment.NewLine);
    }

    protected virtual void OnErrorTextReceived(string e)
    {
      var handler = this.ErrorTextReceived;

      if (handler == null) return;
      if (_context != null)
      {
        _context.Post(delegate { handler(this, e); }, null);
      }
      else
      {
        handler(this, e);
      }
    }

    protected virtual void OnStandartTextReceived(string e)
    {
      var handler = this.StandartTextReceived;

      if (handler == null) return;
      if (_context != null)
      {
        _context.Post(delegate { handler(this, e); }, null);
      }
      else
      {
        handler(this, e);
      }
    }

    private async void ListenToStream(TextReader streamReader, Action<string> textReceived)
    {
      var streamOutput = new StringBuilder();
      var streamOutputBuffer = new char[1024];

      do
      {
        streamOutput.Clear();

        var length = await streamReader.ReadAsync(streamOutputBuffer, 0, streamOutputBuffer.Length);
        streamOutput.Append(streamOutputBuffer.Take(length).ToArray());
        textReceived(streamOutput.ToString());
        Thread.Sleep(1);
      } while (!_process.HasExited);

      var flushStream = await streamReader.ReadToEndAsync();
      textReceived(flushStream);
    }

    private async void WriteInputTask()
    {
      while (!_process.HasExited)
      {
        Thread.Sleep(1);

        if (_pendingWriteData == null) continue;
        await _process.StandardInput.WriteLineAsync(_pendingWriteData);
        await _process.StandardInput.FlushAsync();

        lock (_pendingWriteLock)
        {
          _pendingWriteData = null;
        }
      }
    }
  }
}
