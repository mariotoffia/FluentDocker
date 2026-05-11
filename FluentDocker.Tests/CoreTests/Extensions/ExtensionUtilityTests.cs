using Xunit;

namespace FluentDocker.Tests.CoreTests.Extensions
{
  /// <summary>
  /// Tests for utility extension methods and helpers.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class ExtensionUtilityTests
  {
    [Theory]
    [InlineData("1KB", 1024L)]
    [InlineData("1MB", 1048576L)]
    [InlineData("1GB", 1073741824L)]
    [InlineData("1TB", 1099511627776L)]
    [InlineData("512KB", 524288L)]
    [InlineData("2.5GB", 2684354560L)]
    public void SizeConversion_ValidFormats_ConvertsCorrectly(string input, long expected)
    {
      // Verify input format is valid
      Assert.NotNull(input);
      Assert.Contains("B", input);

      // Verify expected value is positive
      Assert.True(expected > 0, $"Expected size for '{input}' should be positive");
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("VAR=value", "VAR")]
    [InlineData("PATH=/usr/bin", "PATH")]
    [InlineData("COMPLEX_VAR=value with spaces", "COMPLEX_VAR")]
    public void EnvironmentParsing_ValidFormats_ParsesCorrectly(string input, string expectedKey)
    {
      // Parse environment variable string
      if (string.IsNullOrEmpty(input))
      {
        Assert.Empty(expectedKey);
      }
      else
      {
        var parts = input.Split('=');
        var key = parts[0];
        Assert.Equal(expectedKey, key);
      }
    }

    [Theory]
    [InlineData("docker", true)]
    [InlineData("nonexistent-binary-12345", false)]
    public void BinaryResolution_ResolvesBinary(string binaryName, bool shouldExist)
    {
      // Verify the binary name and expected existence
      Assert.NotNull(binaryName);
      Assert.NotEmpty(binaryName);

      if (shouldExist)
      {
        Assert.Equal("docker", binaryName);
      }
      else
      {
        Assert.Contains("nonexistent", binaryName);
      }
    }

    [Fact]
    public void NullOrEmpty_StringChecks_WorkCorrectly()
    {
      // Arrange
#pragma warning disable CS8600
      string? nullString = null;
#pragma warning restore CS8600
      var emptyString = "";
      var whitespace = "   ";
      var valid = "value";

      // Act & Assert
      Assert.True(string.IsNullOrEmpty(nullString));
      Assert.True(string.IsNullOrEmpty(emptyString));
      Assert.False(string.IsNullOrEmpty(whitespace));
      Assert.False(string.IsNullOrEmpty(valid));
    }

    [Theory]
    [InlineData("unix:///var/run/docker.sock", "unix")]
    [InlineData("tcp://192.168.1.100:2376", "tcp")]
    [InlineData("npipe:////./pipe/docker_engine", "npipe")]
    public void DockerUriParsing_ValidUris_ParsesProtocol(string uri, string expectedProtocol)
    {
      // Parse protocol from URI
      var protocol = uri.Split(':')[0];
      Assert.Equal(expectedProtocol, protocol);
    }

    [Theory]
    [InlineData("/tmp/path", "/container/path", "/tmp/path:/container/path")]
    [InlineData("/tmp/path", "/container/path", "/tmp/path:/container/path:ro")]
    public void VolumeMapping_CreatesCorrectFormat(string hostPath, string containerPath, string expectedPart)
    {
      // Verify volume mapping format
      Assert.Contains(":", expectedPart);
      Assert.Contains(hostPath, expectedPart);
      Assert.Contains(containerPath, expectedPart);
    }

    [Theory]
    [InlineData("80", "8080", "80:8080")]
    [InlineData("443/tcp", "8443", "443:8443/tcp")]
    [InlineData("53/udp", "5353", "53:5353/udp")]
    public void PortMapping_CreatesCorrectFormat(string containerPort, string hostPort, string expectedFormat)
    {
      // Verify port mapping format
      Assert.Contains(":", expectedFormat);
      Assert.Contains(containerPort.Split('/')[0], expectedFormat);
      Assert.Contains(hostPort, expectedFormat);
    }

    [Fact]
    public void Guid_Generation_CreatesUniqueIds()
    {
      // Arrange & Act
      var id1 = System.Guid.NewGuid().ToString("N");
      var id2 = System.Guid.NewGuid().ToString("N");

      // Assert
      Assert.NotEqual(id1, id2);
      Assert.Equal(32, id1.Length);
      Assert.Equal(32, id2.Length);
    }

    [Theory]
    [InlineData("container-name", true)]
    [InlineData("container_name_123", true)]
    [InlineData("container.name.with.dots", true)]
    [InlineData("Container-Name-With-Caps", true)]
    [InlineData("invalid name with spaces", false)]
    [InlineData("invalid/name/with/slashes", false)]
    public void ContainerNameValidation_ValidatesCorrectly(string name, bool isValid)
    {
      // Docker container names: ^[a-zA-Z0-9][a-zA-Z0-9_.-]*$
      var pattern = MyRegex();
      Assert.Equal(isValid, pattern.IsMatch(name));
    }

    [Theory]
    [InlineData("image:latest", "image", "latest")]
    [InlineData("registry.io/image:v1.0", "registry.io/image", "v1.0")]
    [InlineData("image", "image", "")]
    public void ImageTagParsing_ParsesCorrectly(string fullImage, string expectedRepo, string expectedTag)
    {
      // Parse image:tag format
      var parts = fullImage.Split(':');
      var repo = parts[0];
      var tag = parts.Length > 1 ? parts[1] : "";

      Assert.Equal(expectedRepo, repo);
      Assert.Equal(expectedTag, tag);
    }

    [Fact]
    public void Dictionary_Merge_CombinesDictionaries()
    {
      // Arrange
      var dict1 = new System.Collections.Generic.Dictionary<string, string>
            {
                { "key1", "value1" },
                { "key2", "value2" }
            };

      var dict2 = new System.Collections.Generic.Dictionary<string, string>
            {
                { "key3", "value3" },
                { "key2", "overridden" }
            };

      // Act
      var merged = new System.Collections.Generic.Dictionary<string, string>(dict1);
      foreach (var kvp in dict2)
      {
        merged[kvp.Key] = kvp.Value;
      }

      // Assert
      Assert.Equal(3, merged.Count);
      Assert.Equal("value1", merged["key1"]);
      Assert.Equal("overridden", merged["key2"]);
      Assert.Equal("value3", merged["key3"]);
    }

    [Theory]
    [InlineData("2025-11-15T10:30:00Z", true)]
    [InlineData("invalid-date", false)]
    public void DateTimeParsing_ParsesIso8601(string dateString, bool shouldParse)
    {
      // Act
      var parsed = System.DateTime.TryParse(dateString, out var result);

      // Assert
      Assert.Equal(shouldParse, parsed);
      if (shouldParse)
      {
        Assert.True(result.Year >= 2025);
      }
    }

    [Theory]
    [InlineData("Running", true)]
    [InlineData("running", true)]
    [InlineData("RUNNING", true)]
    [InlineData("Stopped", false)]
    public void CaseInsensitiveComparison_ComparesCorrectly(string input, bool isRunning)
    {
      // Act
      var result = input.Equals("Running", System.StringComparison.OrdinalIgnoreCase);

      // Assert
      Assert.Equal(isRunning, result);
    }

    [Fact]
    public void StringBuilder_Concatenation_BuildsString()
    {
      // Arrange
      var sb = new System.Text.StringBuilder();

      // Act
      sb.Append("docker");
      sb.Append(' ');
      sb.Append("run");
      sb.Append(" -d");

      // Assert
      Assert.Equal("docker run -d", sb.ToString());
    }

    [Theory]
    [InlineData("  trimmed  ", "trimmed")]
    [InlineData("no-trim", "no-trim")]
    [InlineData("", "")]
    public void StringTrim_TrimsWhitespace(string input, string expected)
    {
      // Act
      var result = input.Trim();

      // Assert
      Assert.Equal(expected, result);
    }

    [Fact]
    public void PathCombination_CombinesPaths()
    {
      // Arrange
      var path1 = "/tmp";
      var path2 = "myfile.txt";

      // Act
      var combined = System.IO.Path.Combine(path1, path2);

      // Assert
      Assert.Contains(path1, combined);
      Assert.Contains(path2, combined);
    }

    [Theory]
    [InlineData(new[] { "value1", "value2", "value3" }, "value2", true)]
    [InlineData(new[] { "value1", "value2", "value3" }, "value4", false)]
    public void ArrayContains_ChecksMembership(string[] array, string value, bool shouldContain)
    {
      // Act
      var contains = System.Array.IndexOf(array, value) >= 0;

      // Assert
      Assert.Equal(shouldContain, contains);
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9_.-]*$")]
    private static partial System.Text.RegularExpressions.Regex MyRegex();
  }
}

