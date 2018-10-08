using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Tests
{
  public sealed class ProcessExecutor
  {
    /// <summary>
    ///   Runs the specified executable with the provided arguments and returns the process' exit code.
    /// </summary>
    /// <param name="output">Recieves the output of either std/err or std/out</param>
    /// <param name="input">Provides the line-by-line input that will be written to std/in, null for empty</param>
    /// <param name="exe">The executable to run, may be unqualified or contain environment variables</param>
    /// <param name="args">The list of unescaped arguments to provide to the executable</param>
    /// <returns>Returns process' exit code after the program exits</returns>
    /// <exception cref="System.IO.FileNotFoundException">Raised when the exe was not found</exception>
    /// <exception cref="System.ArgumentNullException">Raised when one of the arguments is null</exception>
    /// <exception cref="System.ArgumentOutOfRangeException">Raised if an argument contains '\0', '\r', or '\n'
    public static int Run(Action<string> output, TextReader input, string exe, params string[] args)
    {
      if (string.IsNullOrEmpty(exe))
        throw new FileNotFoundException();
      if (output == null)
        throw new ArgumentNullException("output");

      var psi = new ProcessStartInfo
      {
        UseShellExecute = false,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        RedirectStandardInput = true,
        WindowStyle = ProcessWindowStyle.Normal,
        CreateNoWindow = false,
        ErrorDialog = false,
        WorkingDirectory = Environment.CurrentDirectory,
        FileName = FindExePath(exe),
        Arguments = EscapeArguments(args)
      };

      using (var process = Process.Start(psi))
      using (ManualResetEvent mreOut = new ManualResetEvent(false),
        mreErr = new ManualResetEvent(false))
      {
        process.OutputDataReceived += (o, e) =>
        {
          if (e.Data == null) mreOut.Set();
          else output(e.Data);
        };
        
        //process.BeginOutputReadLine() and replace it with the following:
        new ReadOutput(process.StandardOutput, mreOut);

        process.ErrorDataReceived += (o, e) =>
        {
          if (e.Data == null) mreErr.Set();
          else output(e.Data);
        };
        process.BeginErrorReadLine();

        if (null != input)
        {
          string line;
          while (null != (line = input.ReadLine()))
            process.StandardInput.WriteLine(line);
        }        

        //process.StandardInput.Close();
        process.WaitForExit();

        mreOut.WaitOne();
        mreErr.WaitOne();
        return process.ExitCode;
      }
    }

    private static string EscapeArguments(params string[] args)
    {
      var arguments = new StringBuilder();
      var invalidChar = new Regex("[\x00\x0a\x0d]"); //  these can not be escaped
      var needsQuotes = new Regex(@"\s|"""); //          contains whitespace or two quote characters
      var escapeQuote = new Regex(@"(\\*)(""|$)"); //    one or more '\' followed with a quote or end of string

      for (var carg = 0; args != null && carg < args.Length; carg++)
      {
        if (args[carg] == null) throw new ArgumentNullException("args[" + carg + "]");
        if (invalidChar.IsMatch(args[carg])) throw new ArgumentOutOfRangeException("args[" + carg + "]");
        if (args[carg] == string.Empty)
        {
          arguments.Append("\"\"");
        }
        else if (!needsQuotes.IsMatch(args[carg]))
        {
          arguments.Append(args[carg]);
        }
        else
        {
          arguments.Append('"');
          arguments.Append(escapeQuote.Replace(args[carg],
            m => m.Groups[1].Value + m.Groups[1].Value + (m.Groups[2].Value == "\"" ? "\\\"" : "")));
          arguments.Append('"');
        }

        if (carg + 1 < args.Length)
          arguments.Append(' ');
      }

      return arguments.ToString();
    }

    /// <summary>
    ///   Expands environment variables and, if unqualified, locates the exe in the working directory
    ///   or the evironment's path.
    /// </summary>
    /// <param name="exe">The name of the executable file</param>
    /// <returns>The fully-qualified path to the file</returns>
    /// <exception cref="System.IO.FileNotFoundException">Raised when the exe was not found</exception>
    public static string FindExePath(string exe)
    {
      exe = Environment.ExpandEnvironmentVariables(exe);
      if (!File.Exists(exe))
      {
        if (Path.GetDirectoryName(exe) == string.Empty)
          foreach (var test in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
          {
            var path = test.Trim();
            if (!string.IsNullOrEmpty(path) && File.Exists(path = Path.Combine(path, exe)))
              return Path.GetFullPath(path);
          }

        throw new FileNotFoundException(new FileNotFoundException().Message, exe);
      }

      return Path.GetFullPath(exe);
    }

    private sealed class ReadOutput
    {
      private readonly ManualResetEvent _complete;
      private readonly StreamReader _reader;

      public ReadOutput(StreamReader reader, ManualResetEvent complete)
      {
        _reader = reader;
        _complete = complete;
        var t = new Thread(ReadAll);
        t.Start();
      }

      private void ReadAll()
      {
        int ch;
        while (-1 != (ch = _reader.Read())) Console.Write((char) ch);
        _complete.Set();
      }
    }
  }
}