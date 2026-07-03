using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Builders.FileBuilder;
using FluentDocker.Model.Common;

namespace FluentDocker.Builders
{
  public sealed partial class DockerfileBuilder
  {
    private static TemplateString CopyToWorkDir(string source, string workingFolder)
    {
      if (Directory.Exists(source))
        throw new NotSupportedException(
            "Directory sources are not supported by DockerfileBuilder; add files individually.");

      if (!File.Exists(source))
        return source;

      var dest = Path.Combine(workingFolder, Path.GetFileName(source));
      File.Copy(source, dest, true);
      return Path.GetFileName(source);
    }

    private static async Task DownloadFileAsync(
        Uri url, string destinationPath, CancellationToken cancellationToken)
    {
      using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(100));
      using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
          cancellationToken, timeoutCts.Token);
      var response = await Common.SharedHttpClient.Instance.GetAsync(url, linkedCts.Token)
          .ConfigureAwait(false);
      response.EnsureSuccessStatusCode();

      var content = await response.Content.ReadAsByteArrayAsync(linkedCts.Token).ConfigureAwait(false);
      await File.WriteAllBytesAsync(destinationPath, content, linkedCts.Token).ConfigureAwait(false);
    }

    #region Private Methods

    private async Task CopyToWorkDirAsync(string workingFolder, CancellationToken cancellationToken)
    {
      if (!Directory.Exists(workingFolder))
        Directory.CreateDirectory(workingFolder);

      // Copy all files from copy arguments
      var rootedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var cp in _config.Commands.Where(x => x is CopyCommand).Cast<CopyCommand>())
      {
        if (cp is CopyURLCommand urlCmd)
        {
          var wdlp = Path.Combine(workingFolder, cp.From);
          var dd = Path.GetDirectoryName(wdlp);

          if (!string.IsNullOrEmpty(dd) && !Directory.Exists(dd))
            Directory.CreateDirectory(dd);

          await DownloadFileAsync(urlCmd.FromURL, wdlp, cancellationToken).ConfigureAwait(false);
          continue;
        }

        // Standard CopyCommand
        var from = cp.From.Trim('"');
        if (Path.IsPathRooted(from))
        {
          if (Directory.Exists(from))
            throw new NotSupportedException(
                "Directory sources are not supported by DockerfileBuilder; add files individually.");
          var name = Path.GetFileName(from);
          if (!rootedNames.Add(name))
            throw new NotSupportedException(
                $"Multiple rooted COPY sources share the file name '{name}'; rename the sources.");
          if (File.Exists(from))
            File.Copy(from, Path.Combine(workingFolder, name), true);
          cp.From = name;
          continue;
        }

        if (Directory.Exists(from))
          throw new NotSupportedException(
              "Directory sources are not supported by DockerfileBuilder; add files individually.");
        if (!File.Exists(from))
          continue;

        var wp = Path.Combine(workingFolder, from);
        var wdp = Path.GetDirectoryName(wp);
        if (!string.IsNullOrEmpty(wdp) && !Directory.Exists(wdp))
          Directory.CreateDirectory(wdp);

        File.Copy(from, wp, true);
      }

      foreach (var command in _config.Commands.Where(x => x is AddCommand).Cast<AddCommand>())
      {
        var source = command.Source.Rendered;
        var wff = Path.IsPathRooted(source)
            ? Path.Combine(workingFolder, Path.GetFileName(source))
            : Path.Combine(workingFolder, source);
        if (File.Exists(wff) || Directory.Exists(wff))
          continue;

        // Copy to working folder
        _addSourceOverrides[command] = CopyToWorkDir(source, workingFolder);
      }
    }

    private void RenderDockerfile(string workingFolder)
    {
      if (!Directory.Exists(workingFolder))
        Directory.CreateDirectory(workingFolder);

      var dockerFile = Path.Combine(workingFolder, "Dockerfile");

      var contents = !string.IsNullOrEmpty(_config.UseFile?.Rendered)
          ? File.ReadAllText(_config.UseFile)
          : ResolveOrBuildString();

      File.WriteAllText(dockerFile, contents);
      _lastContents = contents;
    }

    private string ResolveOrBuildString()
    {
      var originals = new Dictionary<AddCommand, TemplateString>();
      foreach (var (command, source) in _addSourceOverrides)
      {
        originals[command] = command.Source;
        command.Source = source;
      }

      try
      {
        return !string.IsNullOrWhiteSpace(_config.DockerFileString)
            ? _config.DockerFileString
            : _config.ToString();
      }
      finally
      {
        foreach (var (command, source) in originals)
          command.Source = source;
      }
    }

    #endregion
  }
}
