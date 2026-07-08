using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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

    internal void DeleteOwnedWorkingFolder()
    {
      if (!_ownsWorkingFolder || IsInPlaceBuild || string.IsNullOrWhiteSpace(_workingFolder))
        return;
      try
      {
        if (Directory.Exists(_workingFolder))
          Directory.Delete(_workingFolder, recursive: true);
      }
      catch
      {
        // Best-effort cleanup only.
      }
    }

    private static async Task DownloadFileAsync(
        Uri url, string destinationPath, CancellationToken cancellationToken)
    {
      using var response = await Common.SharedHttpClient.Instance.GetAsync(
          url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
      response.EnsureSuccessStatusCode();

      await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
      await using var destination = File.Create(destinationPath);
      await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    #region Private Methods

    private async Task CopyToWorkDirAsync(
        string workingFolder, bool strictCopySources, CancellationToken cancellationToken)
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
        if (!string.IsNullOrEmpty(cp.Alias))
          continue;
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
          if (!File.Exists(from))
          {
            if (!strictCopySources)
              continue;
            throw new FluentDockerException($"COPY source '{from}' not found");
          }
          File.Copy(from, Path.Combine(workingFolder, name), true);
          _copySourceOverrides[cp] = name;
          continue;
        }

        if (Directory.Exists(from))
          throw new NotSupportedException(
              "Directory sources are not supported by DockerfileBuilder; add files individually.");
        if (!File.Exists(from))
        {
          if (!strictCopySources)
            continue;
          throw new FluentDockerException($"COPY source '{from}' not found");
        }

        var wp = ResolveOwnedContextPath(workingFolder, from);
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
        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
          continue;
        if (Directory.Exists(source))
          throw new NotSupportedException(
              "Directory sources are not supported by DockerfileBuilder; add files individually.");
        if (!File.Exists(source))
        {
          if (!strictCopySources)
            continue;
          throw new FluentDockerException($"ADD source '{source}' not found");
        }

        // Copy to working folder
        _addSourceOverrides[command] = CopyToWorkDir(source, workingFolder);
      }
    }

    private static string ResolveOwnedContextPath(string workingFolder, string relativePath)
    {
      // Lexical containment only: GetFullPath normalises "." / ".." segments but does NOT resolve
      // symlinks, and the comparison is Ordinal (case-sensitive). This suffices for the dev-owned
      // build-context threat model (guarding accidental "../" escapes), not a sandbox for
      // untrusted paths.
      var root = Path.GetFullPath(workingFolder);
      var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
          ? root
          : root + Path.DirectorySeparatorChar;
      var destination = Path.GetFullPath(Path.Combine(root, relativePath));
      if (!destination.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        throw new FluentDockerException(
            $"COPY source '{relativePath}' escapes the build context");
      return destination;
    }

    private async Task RenderDockerfileAsync(string workingFolder, CancellationToken cancellationToken)
    {
      if (!Directory.Exists(workingFolder))
        Directory.CreateDirectory(workingFolder);

      var dockerFile = Path.Combine(workingFolder, "Dockerfile");

      var contents = !string.IsNullOrEmpty(_config.UseFile?.Rendered)
          ? await File.ReadAllTextAsync(_config.UseFile, cancellationToken).ConfigureAwait(false)
          : ResolveOrBuildString();

      await File.WriteAllTextAsync(dockerFile, contents, cancellationToken).ConfigureAwait(false);
      _lastContents = contents;
    }

    private string ResolveOrBuildString()
    {
      var originals = new Dictionary<AddCommand, TemplateString>();
      var copyOriginals = new Dictionary<CopyCommand, string>();
      foreach (var (command, source) in _addSourceOverrides)
      {
        originals[command] = command.Source;
        command.Source = source;
      }
      foreach (var (command, source) in _copySourceOverrides)
      {
        copyOriginals[command] = command.From;
        command.From = source;
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
        foreach (var (command, source) in copyOriginals)
          command.From = source;
      }
    }

    #endregion
  }
}
