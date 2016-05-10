using System;

namespace Ductus.FluentDocker.Model.Common
{
  public sealed class EmbeddedUri : Uri
  {
    public static string Prefix = "emb";

    /// <summary>
    ///   Uri to use when manageing embedded resources.
    /// </summary>
    /// <param name="embedded">Uri on format embedded:AssemblyName/namespace/resource</param>
    public EmbeddedUri(string embedded) : base(embedded)
    {
      var split = embedded.Split(':');
      if (split[0].ToLower() != Prefix)
      {
        throw new ArgumentException($"Incorrect scheme (expecting: {Prefix}) for embedded uri - {embedded}",
          nameof(embedded));
      }

      var s = split[1].Split('/');
      Host = s[0];
      Namespace = s[1];

      if (s.Length > 2)
      {
        Resource = s[2];
      }
    }

    public new string Host { get; }

    public string Assembly => Host;
    public string Namespace { get; }
    public string Resource { get; }

    public static implicit operator EmbeddedUri(string uri)
    {
      if (null == uri)
      {
        return null;
      }

      return new EmbeddedUri(uri);
    }
  }
}