using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ductus.FluentDocker.Internal
{
  internal class DockerVolumeMount
  {
    internal string Host { get; set; }
    internal string Docker { get; set; }
    internal string Acess { get; set; }

    public override string ToString()
    {
      return $"{Host}:{Docker}:{Acess}";
    }

    public static DockerVolumeMount ToMount(string volume)
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

      if (Path.DirectorySeparatorChar == '\\')
      {
        split[0] = "//" + char.ToLower(split[0][0]) + split[0].Substring(2).Replace('\\', '/');
      }

      return split.Length == 3
        ? new DockerVolumeMount {Host = split[0], Docker = split[1], Acess = split[2]}
        : new DockerVolumeMount {Host = split[0], Docker = split[1], Acess = "rw"};
    }

    public static IList<DockerVolumeMount> ToMount(string[] volume)
    {
      return volume.Select(ToMount).ToList();
    }

    public static string[] ToStringArray(DockerVolumeMount[] mounts)
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