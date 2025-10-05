using FluentAssertions;
using ServiceBusCli.Core;

namespace ServiceBusCli.Core.Tests;

public class ParserTests
{
    [Fact]
    public void Parses_open_with_number()
    {
        var cmd = CommandParser.Parse("open 12");
        cmd.Kind.Should().Be(CommandKind.Open);
        cmd.Index.Should().Be(12);
    }

    [Fact]
    public void Parses_quit_aliases()
    {
        CommandParser.Parse("q").Kind.Should().Be(CommandKind.Quit);
        CommandParser.Parse("quit").Kind.Should().Be(CommandKind.Quit);
        CommandParser.Parse("exit").Kind.Should().Be(CommandKind.Quit);
    }

    [Theory]
    [InlineData("h")]
    [InlineData("help")]
    [InlineData("?")]
    public void Parses_help(string input)
    {
        CommandParser.Parse(input).Kind.Should().Be(CommandKind.Help);
    }
}

public class TextTruncationTests
{
    [Fact]
    public void Truncates_with_ellipsis()
    {
        TextTruncation.Truncate("abcdef", 4).Should().Be("abc…");
    }

    [Fact]
    public void Returns_input_if_fits()
    {
        TextTruncation.Truncate("abc", 5).Should().Be("abc");
    }
}

