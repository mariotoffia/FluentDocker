using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
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
  }
}
