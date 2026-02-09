using FluentDocker.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
    /// <summary>
    /// Unit tests for <see cref="ShellArgParser"/>.
    /// </summary>
    [Trait("Category", "Unit")]
    public class ShellArgParserTests
    {
        [Fact]
        public void Parse_SimpleCommand_SplitsIntoArgs()
        {
            // Arrange & Act
            var result = ShellArgParser.Parse("ls -la /path");

            // Assert
            Assert.Equal(3, result.Length);
            Assert.Equal("ls", result[0]);
            Assert.Equal("-la", result[1]);
            Assert.Equal("/path", result[2]);
        }

        [Fact]
        public void Parse_DoubleQuotedArg_PreservesSpaces()
        {
            // Arrange & Act
            var result = ShellArgParser.Parse("echo \"hello world\"");

            // Assert
            Assert.Equal(2, result.Length);
            Assert.Equal("echo", result[0]);
            Assert.Equal("hello world", result[1]);
        }

        [Fact]
        public void Parse_SingleQuotedArg_PreservesSpaces()
        {
            // Arrange & Act
            var result = ShellArgParser.Parse("echo 'hello world'");

            // Assert
            Assert.Equal(2, result.Length);
            Assert.Equal("echo", result[0]);
            Assert.Equal("hello world", result[1]);
        }

        [Fact]
        public void Parse_ShDashC_KeepsQuotedCommandAsOneArg()
        {
            // Arrange & Act
            var result = ShellArgParser.Parse("sh -c \"ls -la /path\"");

            // Assert
            Assert.Equal(3, result.Length);
            Assert.Equal("sh", result[0]);
            Assert.Equal("-c", result[1]);
            Assert.Equal("ls -la /path", result[2]);
        }

        [Fact]
        public void Parse_MultipleQuotedArgs_EachPreserved()
        {
            // Arrange & Act
            var result = ShellArgParser.Parse("cmd \"arg one\" \"arg two\"");

            // Assert
            Assert.Equal(3, result.Length);
            Assert.Equal("cmd", result[0]);
            Assert.Equal("arg one", result[1]);
            Assert.Equal("arg two", result[2]);
        }

        [Fact]
        public void Parse_MixedSingleAndDoubleQuotes_BothPreserved()
        {
            // Arrange & Act
            var result = ShellArgParser.Parse("cmd 'single quoted' \"double quoted\"");

            // Assert
            Assert.Equal(3, result.Length);
            Assert.Equal("cmd", result[0]);
            Assert.Equal("single quoted", result[1]);
            Assert.Equal("double quoted", result[2]);
        }

        [Fact]
        public void Parse_ExtraSpaces_Ignored()
        {
            // Arrange & Act
            var result = ShellArgParser.Parse("  ls   -la   /path  ");

            // Assert
            Assert.Equal(3, result.Length);
            Assert.Equal("ls", result[0]);
            Assert.Equal("-la", result[1]);
            Assert.Equal("/path", result[2]);
        }

        [Fact]
        public void Parse_Null_ReturnsEmptyArray()
        {
            // Arrange & Act
            var result = ShellArgParser.Parse(null);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void Parse_EmptyString_ReturnsEmptyArray()
        {
            // Arrange & Act
            var result = ShellArgParser.Parse("");

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void Parse_WhitespaceOnly_ReturnsEmptyArray()
        {
            // Arrange & Act
            var result = ShellArgParser.Parse("   \t  ");

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void Parse_SingleArg_ReturnsSingleElement()
        {
            // Arrange & Act
            var result = ShellArgParser.Parse("hello");

            // Assert
            Assert.Single(result);
            Assert.Equal("hello", result[0]);
        }

        [Fact]
        public void Parse_SingleQuoteInsideDoubleQuotes_Preserved()
        {
            // Arrange & Act
            var result = ShellArgParser.Parse("cmd \"it's working\"");

            // Assert
            Assert.Equal(2, result.Length);
            Assert.Equal("cmd", result[0]);
            Assert.Equal("it's working", result[1]);
        }

        [Fact]
        public void Parse_DoubleQuoteInsideSingleQuotes_Preserved()
        {
            // Arrange & Act
            var result = ShellArgParser.Parse("cmd 'say \"hello\"'");

            // Assert
            Assert.Equal(2, result.Length);
            Assert.Equal("cmd", result[0]);
            Assert.Equal("say \"hello\"", result[1]);
        }

        [Fact]
        public void Parse_EscapedQuoteInDoubleQuotes_Preserved()
        {
            // Arrange & Act
            var result = ShellArgParser.Parse("cmd \"say \\\"hello\\\"\"");

            // Assert
            Assert.Equal(2, result.Length);
            Assert.Equal("cmd", result[0]);
            Assert.Equal("say \"hello\"", result[1]);
        }

        [Fact]
        public void Parse_EscapedBackslashInDoubleQuotes_Preserved()
        {
            // Arrange & Act
            var result = ShellArgParser.Parse("cmd \"path\\\\to\"");

            // Assert
            Assert.Equal(2, result.Length);
            Assert.Equal("cmd", result[0]);
            Assert.Equal("path\\to", result[1]);
        }

        [Fact]
        public void Parse_BackslashOutsideQuotes_EscapesNextChar()
        {
            // Arrange & Act
            var result = ShellArgParser.Parse("cmd hello\\ world");

            // Assert
            Assert.Equal(2, result.Length);
            Assert.Equal("cmd", result[0]);
            Assert.Equal("hello world", result[1]);
        }

        [Fact]
        public void Parse_UnmatchedQuote_TreatsRestAsArg()
        {
            // Arrange & Act
            var result = ShellArgParser.Parse("cmd \"unclosed");

            // Assert
            Assert.Equal(2, result.Length);
            Assert.Equal("cmd", result[0]);
            Assert.Equal("unclosed", result[1]);
        }
    }
}
