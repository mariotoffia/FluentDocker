using System;
using System.Net;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  internal static class LoopbackHttpListenerSupport
  {
    // Starts an HttpListener on a free loopback port with no bind/close/rebind gap (TOCTOU-free).
    // Retries a fresh random port on HttpListenerException. baseUrl is "http://127.0.0.1:{port}/".
    //
    // Teardown rule: terminate with listener.Close(), NEVER Stop() followed by Dispose().
    // Stop() empties the endpoint map but leaves Prefixes populated, so the later Dispose()
    // runs a second removal pass in which the managed HttpEndPointManager RE-BINDS the port
    // (no SO_REUSEADDR) — racing anything that holds it and flaking with EADDRINUSE.
    // Close() disposes in one pass and makes the enclosing using-dispose a no-op.
    internal static HttpListener Start(out string baseUrl)
    {
      HttpListenerException last = null;
      for (var attempt = 0; attempt < 40; attempt++)
      {
        var port = Random.Shared.Next(30000, 49000);
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        try
        {
          listener.Start();
          baseUrl = $"http://127.0.0.1:{port}/";
          return listener;
        }
        catch (HttpListenerException ex)
        {
          last = ex;
          listener.Close();
        }
      }

      // ponytail: retrying the returned listener itself closes the TCP TOCTOU.
      throw last;
    }
  }
}
