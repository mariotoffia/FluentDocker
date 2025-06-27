using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.Extensions
{
  public static class HttpExtensions
  {
    public static async Task<string> Wget(this string path, bool assertOk = true, bool assertDataStream = true)
    {
#if NETSTANDARD2_0
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
#else
      using (var httpClient = new HttpClient())
      {
        var response = await httpClient.GetAsync(path);
        
        if (assertOk)
        {
          Assert.AreEqual("OK", response.ReasonPhrase);
        }
        
        var stream = await response.Content.ReadAsStreamAsync();
        
        if (assertDataStream)
        {
          Assert.IsNotNull(stream);
        }
        
        if (stream == null)
        {
          return string.Empty;
        }
        
        using (var reader = new StreamReader(stream))
        {
          var responseFromServer = await reader.ReadToEndAsync();
          return responseFromServer;
        }
      }
#endif
    }
  }
}
