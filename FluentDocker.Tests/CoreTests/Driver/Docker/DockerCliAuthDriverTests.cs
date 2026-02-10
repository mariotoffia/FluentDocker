using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  [Trait("Category", "Unit")]
  public class DockerCliAuthDriverTests
  {
    #region BuildLoginArgs — Password Inline

    [Fact]
    public void BuildLoginArgs_PasswordInline_ContainsPasswordFlag()
    {
      var config = new RegistryLoginConfig
      {
        Username = "user1",
        Password = "secret",
        PasswordStdin = false
      };

      var (args, stdinData) = DockerCliAuthDriver.BuildLoginArgs(config);

      Assert.Contains("-p secret", args);
      Assert.DoesNotContain("--password-stdin", args);
      Assert.Null(stdinData);
    }

    #endregion

    #region BuildLoginArgs — Password Stdin

    [Fact]
    public void BuildLoginArgs_PasswordStdin_ContainsStdinFlag()
    {
      var config = new RegistryLoginConfig
      {
        Username = "user1",
        Password = "secret",
        PasswordStdin = true
      };

      var (args, stdinData) = DockerCliAuthDriver.BuildLoginArgs(config);

      Assert.Contains("--password-stdin", args);
      Assert.DoesNotContain("-p ", args);
      Assert.Equal("secret", stdinData);
    }

    [Fact]
    public void BuildLoginArgs_PasswordStdinNoPassword_StdinDataIsNull()
    {
      var config = new RegistryLoginConfig
      {
        Username = "user1",
        PasswordStdin = true
      };

      var (args, stdinData) = DockerCliAuthDriver.BuildLoginArgs(config);

      Assert.Contains("--password-stdin", args);
      Assert.Null(stdinData);
    }

    #endregion

    #region BuildLoginArgs — Server

    [Fact]
    public void BuildLoginArgs_WithServer_ServerAppendedAtEnd()
    {
      var config = new RegistryLoginConfig
      {
        Username = "user1",
        Password = "pass",
        Server = "registry.example.com"
      };

      var (args, _) = DockerCliAuthDriver.BuildLoginArgs(config);

      Assert.EndsWith("registry.example.com", args);
    }

    [Fact]
    public void BuildLoginArgs_NoServer_NoServerInArgs()
    {
      var config = new RegistryLoginConfig
      {
        Username = "user1",
        Password = "pass"
      };

      var (args, _) = DockerCliAuthDriver.BuildLoginArgs(config);

      Assert.Equal("login -u user1 -p pass", args);
    }

    #endregion

    #region BuildLoginArgs — Username

    [Fact]
    public void BuildLoginArgs_NoUsername_NoUserFlag()
    {
      var config = new RegistryLoginConfig
      {
        Password = "pass"
      };

      var (args, _) = DockerCliAuthDriver.BuildLoginArgs(config);

      Assert.DoesNotContain("-u ", args);
      Assert.Contains("-p pass", args);
    }

    [Fact]
    public void BuildLoginArgs_WithUsername_ContainsUserFlag()
    {
      var config = new RegistryLoginConfig
      {
        Username = "admin"
      };

      var (args, _) = DockerCliAuthDriver.BuildLoginArgs(config);

      Assert.Contains("-u admin", args);
    }

    #endregion

    #region BuildLoginArgs — Edge Cases

    [Fact]
    public void BuildLoginArgs_NoCredentials_JustLogin()
    {
      var config = new RegistryLoginConfig();

      var (args, stdinData) = DockerCliAuthDriver.BuildLoginArgs(config);

      Assert.Equal("login", args);
      Assert.Null(stdinData);
    }

    [Fact]
    public void BuildLoginArgs_AllFields_CorrectOrder()
    {
      var config = new RegistryLoginConfig
      {
        Username = "user1",
        Password = "pass",
        PasswordStdin = false,
        Server = "ghcr.io"
      };

      var (args, _) = DockerCliAuthDriver.BuildLoginArgs(config);

      // Expected: login -u user1 -p pass ghcr.io
      Assert.StartsWith("login", args);
      var uPos = args.IndexOf("-u user1");
      var pPos = args.IndexOf("-p pass");
      var sPos = args.IndexOf("ghcr.io");
      Assert.True(uPos < pPos, "Username should come before password");
      Assert.True(pPos < sPos, "Password should come before server");
    }

    [Fact]
    public void BuildLoginArgs_StdinWithServer_CorrectOrder()
    {
      var config = new RegistryLoginConfig
      {
        Username = "user1",
        Password = "secret",
        PasswordStdin = true,
        Server = "docker.io"
      };

      var (args, stdinData) = DockerCliAuthDriver.BuildLoginArgs(config);

      Assert.Equal("login -u user1 --password-stdin docker.io", args);
      Assert.Equal("secret", stdinData);
    }

    #endregion
  }
}
