using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Integration tests for IAuthDriver (Docker CLI).
  /// Requires Docker to be running.
  /// Tests are marked DevLocal because they start a local registry:2 with htpasswd auth.
  /// </summary>
  [Trait("Category", "DevLocal")]
  public class AuthDriverTests : DockerDriverTestBase
  {
    private IAuthDriver AuthDriver => Kernel.SysCtl<IAuthDriver>(DriverId);

    private const string RegistryPort = "5060";
    private const string TestUser = "testuser";
    private const string TestPassword = "testpass123";

    /// <summary>
    /// Login to a local registry with valid credentials succeeds.
    /// </summary>
    [Fact]
    public async Task Login_ValidCredentials_Succeeds()
    {
      string registryId = null;
      string tempDir = null;

      try
      {
        (registryId, tempDir) = await StartAuthRegistryAsync(RegistryPort);

        var result = await AuthDriver.LoginAsync(Context, new RegistryLoginConfig
        {
          Server = $"localhost:{RegistryPort}",
          Username = TestUser,
          Password = TestPassword
        }, TestContext.Current.CancellationToken);

        Assert.True(result.Success, $"Login failed: {result.Error}");
      }
      finally
      {
        // Logout to clean credential store
        try
        {
          await AuthDriver.LogoutAsync(Context, $"localhost:{RegistryPort}", TestContext.Current.CancellationToken);
        }
        catch { }

        await RemoveContainerAsync(registryId);
        CleanupTempDir(tempDir);
      }
    }

    /// <summary>
    /// Login with wrong password fails gracefully.
    /// </summary>
    [Fact]
    public async Task Login_InvalidPassword_Fails()
    {
      string registryId = null;
      string tempDir = null;

      try
      {
        (registryId, tempDir) = await StartAuthRegistryAsync("5061");

        var result = await AuthDriver.LoginAsync(Context, new RegistryLoginConfig
        {
          Server = "localhost:5061",
          Username = TestUser,
          Password = "wrongpassword"
        }, TestContext.Current.CancellationToken);

        Assert.False(result.Success, "Login should fail with wrong password");
      }
      finally
      {
        await RemoveContainerAsync(registryId);
        CleanupTempDir(tempDir);
      }
    }

    /// <summary>
    /// Logout from a registry succeeds (even if not previously logged in).
    /// </summary>
    [Fact]
    public async Task Logout_AfterLogin_Succeeds()
    {
      string registryId = null;
      string tempDir = null;

      try
      {
        (registryId, tempDir) = await StartAuthRegistryAsync("5062");

        // Login first
        var loginResult = await AuthDriver.LoginAsync(Context, new RegistryLoginConfig
        {
          Server = "localhost:5062",
          Username = TestUser,
          Password = TestPassword
        }, TestContext.Current.CancellationToken);
        Assert.True(loginResult.Success, $"Login failed: {loginResult.Error}");

        // Logout
        var logoutResult = await AuthDriver.LogoutAsync(Context, "localhost:5062", TestContext.Current.CancellationToken);
        Assert.True(logoutResult.Success, $"Logout failed: {logoutResult.Error}");
      }
      finally
      {
        await RemoveContainerAsync(registryId);
        CleanupTempDir(tempDir);
      }
    }

    /// <summary>
    /// Login with PasswordStdin uses stdin for the password.
    /// </summary>
    [Fact]
    public async Task Login_WithPasswordStdin_Succeeds()
    {
      string registryId = null;
      string tempDir = null;

      try
      {
        (registryId, tempDir) = await StartAuthRegistryAsync("5063");

        var result = await AuthDriver.LoginAsync(Context, new RegistryLoginConfig
        {
          Server = "localhost:5063",
          Username = TestUser,
          Password = TestPassword,
          PasswordStdin = true
        }, TestContext.Current.CancellationToken);

        Assert.True(result.Success, $"Login via stdin failed: {result.Error}");
      }
      finally
      {
        try
        {
          await AuthDriver.LogoutAsync(Context, "localhost:5063", TestContext.Current.CancellationToken);
        }
        catch { }

        await RemoveContainerAsync(registryId);
        CleanupTempDir(tempDir);
      }
    }

    #region Helpers

    /// <summary>
    /// Starts a registry:2 container with basic auth (htpasswd).
    /// Returns (containerId, tempDir).
    /// </summary>
    private async Task<(string containerId, string tempDir)> StartAuthRegistryAsync(
        string hostPort)
    {
      var tempDir = Path.Combine(Path.GetTempPath(), $"auth-reg-{Guid.NewGuid():N}");
      var authDir = Path.Combine(tempDir, "auth");
      Directory.CreateDirectory(authDir);

      // Generate htpasswd file using the registry image's htpasswd utility
      var htpasswdFile = Path.Combine(authDir, "htpasswd");
      await GenerateHtpasswdAsync(htpasswdFile);

      var containerId = await RunContainerAsync("registry:2",
          new ContainerCreateConfig
          {
            PortBindings = new Dictionary<string, string>
            {
              ["5000/tcp"] = hostPort
            },
            Environment = new Dictionary<string, string>
            {
              ["REGISTRY_AUTH"] = "htpasswd",
              ["REGISTRY_AUTH_HTPASSWD_REALM"] = "RegistryRealm",
              ["REGISTRY_AUTH_HTPASSWD_PATH"] = "/auth/htpasswd"
            },
            Volumes = new Dictionary<string, string>
            {
              [authDir] = "/auth"
            }
          });

      // Wait for registry to start
      await Task.Delay(3000);
      return (containerId, tempDir);
    }

    /// <summary>
    /// Generates htpasswd file using httpd:alpine which includes the htpasswd binary.
    /// Uses bcrypt for password hashing.
    /// </summary>
    private async Task GenerateHtpasswdAsync(string outputPath)
    {
      await EnsureImageAsync("httpd:alpine");

      var config = new ContainerCreateConfig
      {
        Image = "httpd:alpine",
        Entrypoint = new[] { "htpasswd" },
        Command = new[] { "-Bbn", TestUser, TestPassword }
      };

      var result = await ContainerDriver.RunAsync(Context, config);
      Assert.True(result.Success, $"htpasswd generation failed: {result.Error}");

      var containerId = result.Data.Id;
      try
      {
        // Wait for the container to finish
        await Task.Delay(2000);

        var logs = await ContainerDriver.GetLogsAsync(
            Context, containerId, tail: 10);
        Assert.True(logs.Success, $"Failed to get htpasswd output: {logs.Error}");

        var htpasswdContent = logs.Data?.Trim();
        Assert.False(string.IsNullOrWhiteSpace(htpasswdContent),
            "htpasswd output should not be empty");

        File.WriteAllText(outputPath, htpasswdContent + Environment.NewLine);
      }
      finally
      {
        await RemoveContainerAsync(containerId);
      }
    }

    private static void CleanupTempDir(string tempDir)
    {
      if (tempDir != null && Directory.Exists(tempDir))
        try
        { Directory.Delete(tempDir, true); }
        catch { }
    }

    #endregion
  }
}
