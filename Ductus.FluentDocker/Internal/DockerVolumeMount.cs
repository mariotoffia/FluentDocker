using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ductus.FluentDocker.Extensions;

namespace Ductus.FluentDocker.Internal
{
  internal class DockerVolumeMount
  {
    /// <summary>
    /// The name of the mounted volyme, if any.
    /// </summary>
    internal string Name { get; set; }

    /// <summary>
    ///   Host path in MSYS or linux compatible format.
    /// </summary>
    internal string Host { get; set; }

    /// <summary>
    ///   Inside docker container path in linux format.
    /// </summary>
    internal string Docker { get; set; }

    /// <summary>
    ///   Which access 'ro' or 'rw'.
    /// </summary>
    internal string Acess { get; set; }

    public override string ToString()
    {
      return $"{Host}:{Docker}:{Acess}";
    }


    internal static DockerVolumeMount ToMount(string volume)
    {
      var split = volume.Split(':');
      if (split.Length < 2)
      {
        throw new FluentDockerException(
          $"Illegal volume string {volume} - expected format is 'local host path':'docker exposed path':ro|rw");
      }

      split[0] = split[0].Render();
      if (split[0][1] == ':')
      {
        Directory.CreateDirectory(split[0]);
      }

      if (OsExtensions.IsWindows())
      {
        // Render MSYS compatible path if Windos path is expressed.
        split[0] = split[0].ToMsysPath();
      }

      return split.Length == 3
        ? new DockerVolumeMount {Host = split[0], Docker = split[1], Acess = split[2]}
        : new DockerVolumeMount {Host = split[0], Docker = split[1], Acess = "rw"};
    }

    internal static IList<DockerVolumeMount> ToMount(string[] volume)
    {
      return volume.Select(ToMount).ToList();
    }

    internal static string[] ToStringArray(DockerVolumeMount[] mounts)
    {
      if (null == mounts || 0 == mounts.Length)
      {
        return null;
      }

      var arr = new string[mounts.Length];
      for (var i = 0; i < arr.Length; i++)
      {
        arr[i] = mounts[i].ToString();
      }

      return arr;
    }
  }
}