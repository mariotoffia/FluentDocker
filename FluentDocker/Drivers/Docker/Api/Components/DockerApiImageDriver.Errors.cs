using System;
using FluentDocker.Common;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  public partial class DockerApiImageDriver
  {
    private string TryExtractErrorMessage(string body)
    {
      if (string.IsNullOrWhiteSpace(body))
        return null;
      try
      {
        var el = JsonHelper.ParseElement(body);
        return el.GetStringOrDefault("message");
      }
      catch (Exception ex)
      {
        if (Logger.IsEnabled(LogLevel.Debug))
          Logger.LogDebug(ex, "Error message JSON parse failed");
        return body.Length > 500 ? body[..500] : body;
      }
    }
  }
}
