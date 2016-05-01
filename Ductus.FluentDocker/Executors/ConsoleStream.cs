using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Ductus.FluentDocker.Common;

namespace Ductus.FluentDocker.Executors
{
  public sealed class ConsoleStream<T> : IDisposable where T : class
  {
    private readonly IStreamMapper<T> _mapper;
    private readonly Process _process;
    private readonly BlockingCollection<T> _values = new BlockingCollection<T>();

    internal ConsoleStream(ProcessStartInfo startInfo, IStreamMapper<T> mapper)
    {
      _mapper = mapper;
      _process = new Process {StartInfo = startInfo};
      _process.OutputDataReceived += (sender, args) =>
      {
        var val = _mapper.OnData(args.Data, false);
        if (null != val)
        {
          _values.Add(val);
        }
        Error = _mapper.Error;
      };

      _process.ErrorDataReceived += (sender, args) =>
      {
        var val = _mapper.OnData(args.Data, true);
        if (null != val)
        {
          _values.Add(val);
        }
        Error = _mapper.Error;
      };

      _process.Exited += (sender, args) =>
      {
        _values.Add(_mapper.OnProcessEnd(_process.ExitCode));
        Error = _mapper.Error;
        IsSuccess = _process.ExitCode == 0;
        IsFinished = true;
      };

      if (!_process.Start())
      {
        throw new FluentDockerException($"Could not start process {startInfo.FileName}");
      }

      _process.BeginOutputReadLine();
      _process.BeginErrorReadLine();
    }

    public string Error { get; private set; }
    public bool IsFinished { get; private set; }
    public bool IsSuccess { get; private set; }
    public void Dispose()
    {
      _process.Dispose();
      _values.Dispose();
    }

    public T Read(CancellationToken cancellationToken = default(CancellationToken))
    {
      if (IsFinished)
      {
        return null;
      }

      return _values.Take(cancellationToken);
    }
  }
}