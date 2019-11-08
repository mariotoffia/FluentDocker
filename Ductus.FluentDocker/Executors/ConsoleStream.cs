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
    private readonly CancellationToken _token;
    private readonly BlockingCollection<T> _values = new BlockingCollection<T>();

    internal ConsoleStream(ProcessStartInfo startInfo, IStreamMapper<T> mapper, CancellationToken token)
    {
      _token = token;
      _mapper = mapper;

      _process = new Process
      {
        StartInfo = startInfo,
        EnableRaisingEvents = true
      };

      if (CancellationToken.None != token)
      {
        token.Register(CancelProcess);
      }

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
        Error = _mapper.Error;
        IsSuccess = _process.ExitCode == 0;
        IsFinished = true;
        _values.Add(_mapper.OnProcessEnd(_process.ExitCode));
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

    public T Read()
    {
      return IsFinished ? null : _values.Take(_token);
    }

    public T TryRead(int millisTimeout)
    {
      T result;
      if (IsFinished)
      {
        if (_values.TryTake(out result, 10, _token))
        {
          return result;
        }
        
        return null;
      }

      
      if (_values.TryTake(out result, millisTimeout, _token))
      {
        return result;
      }
      return null;
    }

    private void CancelProcess()
    {
      if (_process.HasExited)
      {
        return;
      }

      try
      {
        IsSuccess = false;
        IsFinished = true;
        _process.StandardInput.WriteLine(char.ConvertFromUtf32(3));
        _process.Kill();
      }
      catch (Exception e)
      {
        Debug.WriteLine($"Got exception when sending control+c to process - msg: {e.Message}");
      }
    }
  }
}
