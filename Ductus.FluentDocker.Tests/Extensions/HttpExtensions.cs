using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.Extensions
{
  public static class HttpExtensions
  {
    public static async Task<string> Wget(this string path, bool assertOk = true, bool assertDataStream = true)
    {
      var request = WebRequest.Create(path);
      using (var response = await request.GetResponseAsync())
      {
        if (assertOk)
        {
          Assert.AreEqual("OK", ((HttpWebResponse)response).StatusDescription);
        }

        var dataStream = response.GetResponseStream();

        if (assertDataStream)
        {
          Assert.IsNotNull(dataStream);
        }

        if (null == dataStream)
        {
          return string.Empty;
        }

        using (var reader = new StreamReader(dataStream))
        {
          var responseFromServer = reader.ReadToEnd();
          return responseFromServer;
        }
      }
    }
  }
}
