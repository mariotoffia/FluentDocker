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
      using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      downloadCts.CancelAfter(TimeSpan.FromSeconds(100));
      var downloadToken = downloadCts.Token;
      try
      {
        using var response = await Common.SharedHttpClient.Instance.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead, downloadToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(downloadToken).ConfigureAwait(false);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, downloadToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException ex) when (
          !cancellationToken.IsCancellationRequested && downloadCts.IsCancellationRequested)
      {
        DeletePartialDownload(destinationPath);
        throw new FluentDockerException($"Download of '{url}' exceeded 100s", ex);
      }
      catch
      {
        DeletePartialDownload(destinationPath);
        throw;
      }
    }

    private static void DeletePartialDownload(string path)
    {
      try
      {
        if (File.Exists(path))
          File.Delete(path);
      }
      catch
      {
      }
    }

    #region Private Methods

    private void EnsureNoMixedDockerfileSources()
    {
      var hasSource = !string.IsNullOrEmpty(_config.UseFile?.Rendered) ||
          !string.IsNullOrWhiteSpace(_config.DockerFileString);
      if (hasSource && _config.Commands.Count > 0)
        throw new FluentDockerException(
            "FromFile()/FromString() cannot be combined with fluent Dockerfile commands; choose one Dockerfile source.");
    }

    private async Task CopyToWorkDirAsync(
        string workingFolder, bool strictCopySources, CancellationToken cancellationToken)
    {
      if (!Directory.Exists(workingFolder))
        Directory.CreateDirectory(workingFolder);

      // Copy all files from copy arguments
      var rootedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
          if (!File.Exists(from))
          {
            if (!strictCopySources)
              continue;
            throw new FluentDockerException($"COPY source '{from}' not found");
          }
          ClaimRootedName(rootedNames, name, from);
          var rootedCopyDest = Path.Combine(workingFolder, name);
          GuardNoDirectoryAt(rootedCopyDest, name);
          File.Copy(from, rootedCopyDest, true);
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
        // A bare (root-level) relative source stages next to any rooted COPY/ADD, so it must
        // claim its name too or a later rooted source with the same basename would silently
        // ship this file's bytes under its name instead of its own. Claimed only after the
        // existence check so a skipped (lenient, missing) source never reserves a name.
        if (string.IsNullOrEmpty(Path.GetDirectoryName(from)))
          ClaimRootedName(rootedNames, Path.GetFileName(from), from);

        var wp = ResolveOwnedContextPath(workingFolder, from);
        var wdp = Path.GetDirectoryName(wp);
        if (!string.IsNullOrEmpty(wdp) && !Directory.Exists(wdp))
          Directory.CreateDirectory(wdp);

        File.Copy(from, wp, true);
      }

      foreach (var command in _config.Commands.Where(x => x is AddCommand).Cast<AddCommand>())
      {
        var source = command.Source.Rendered;
        if (Path.IsPathRooted(source))
        {
          var name = Path.GetFileName(source);
          if (Directory.Exists(source))
            throw new NotSupportedException(
                "Directory sources are not supported by DockerfileBuilder; add files individually.");
          if (!File.Exists(source))
          {
            if (!strictCopySources)
              continue;
            throw new FluentDockerException($"ADD source '{source}' not found");
          }
          ClaimRootedName(rootedNames, name, source);
          var rootedDest = Path.Combine(workingFolder, name);
          GuardNoDirectoryAt(rootedDest, name);
          File.Copy(source, rootedDest, true);
          _addSourceOverrides[command] = name;
          continue;
        }
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
        if (string.IsNullOrEmpty(Path.GetDirectoryName(source)))
          ClaimRootedName(rootedNames, Path.GetFileName(source), source);

        var wff = ResolveOwnedContextPath(workingFolder, source);
        var wdp = Path.GetDirectoryName(wff);
        if (!string.IsNullOrEmpty(wdp) && !Directory.Exists(wdp))
          Directory.CreateDirectory(wdp);

        File.Copy(source, wff, true);
      }
    }

    /// <summary>
    /// Claims <paramref name="name"/> for <paramref name="source"/> in the shared root-level
    /// namespace, so a later rooted/root-level source with the same basename but a different
    /// identity fails the build early instead of silently overwriting (or losing out to) it.
    /// </summary>
    private static void ClaimRootedName(
        Dictionary<string, string> rootedNames, string name, string source)
    {
      if (rootedNames.TryGetValue(name, out var prior) &&
          !string.Equals(prior, source, StringComparison.Ordinal))
        throw new NotSupportedException(
            $"Multiple rooted COPY/ADD sources share the file name '{name}'; rename the sources.");
      rootedNames[name] = source;
    }

    /// <summary>
    /// Fails with a typed, actionable error (instead of a raw <c>File.Copy</c>
    /// <see cref="UnauthorizedAccessException"/>/<see cref="IOException"/>) when a directory
    /// already occupies the build-context path a rooted source is about to stage into — e.g. an
    /// earlier nested relative source created a directory of the same name at the context root.
    /// </summary>
    private static void GuardNoDirectoryAt(string rootedDest, string name)
    {
      if (Directory.Exists(rootedDest))
        throw new NotSupportedException(
            $"Cannot stage '{name}': a directory already exists at that build-context path " +
            "(a nested source created it); rename the source or the conflicting file.");
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
            $"COPY/ADD source '{relativePath}' escapes the build context");
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
