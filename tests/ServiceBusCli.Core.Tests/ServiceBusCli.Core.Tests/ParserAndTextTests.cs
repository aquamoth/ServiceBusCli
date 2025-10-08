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
        cmd.Index.Should().Be(12L);
    }

    [Fact]
    public void Parses_quit_aliases()
    {
        CommandParser.Parse("q").Kind.Should().Be(CommandKind.Quit);
        CommandParser.Parse("quit").Kind.Should().Be(CommandKind.Quit);
        CommandParser.Parse("exit").Kind.Should().Be(CommandKind.Quit);
    }

    [Fact]
    public void Parses_queue_and_dlq_commands()
    {
        var q = CommandParser.Parse("queue 12");
        q.Kind.Should().Be(CommandKind.Queue);
        q.Index.Should().Be(12);

        var d = CommandParser.Parse("dlq 7");
        d.Kind.Should().Be(CommandKind.Dlq);
        d.Index.Should().Be(7);

        // Bare toggles without index
        CommandParser.Parse("dlq").Kind.Should().Be(CommandKind.Dlq);
        CommandParser.Parse("queue").Kind.Should().Be(CommandKind.Queue);
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
