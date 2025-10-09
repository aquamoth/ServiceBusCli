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

    [Fact]
    public void Parses_reject_command()
    {
        var r = CommandParser.Parse("reject 42");
        r.Kind.Should().Be(CommandKind.Reject);
        r.Index.Should().Be(42);
    }

    [Fact]
    public void Parses_resubmit_command()
    {
        var r = CommandParser.Parse("resubmit 99");
        r.Kind.Should().Be(CommandKind.Resubmit);
        r.Index.Should().Be(99);
    }

    [Fact]
    public void Parses_resubmit_expression()
    {
        var r = CommandParser.Parse("resubmit 10-12, 15");
        r.Kind.Should().Be(CommandKind.Resubmit);
        r.Index.Should().BeNull();
        r.Raw.Should().Be("10-12, 15");
    }

    [Fact]
    public void Parses_delete_command()
    {
        var d = CommandParser.Parse("delete 10");
        d.Kind.Should().Be(CommandKind.Delete);
        d.Index.Should().Be(10);
    }

    [Fact]
    public void Parses_delete_expression()
    {
        var d = CommandParser.Parse("delete 1, 3-4");
        d.Kind.Should().Be(CommandKind.Delete);
        d.Index.Should().BeNull();
        d.Raw.Should().Be("1, 3-4");
    }

    [Fact]
    public void Parses_session_command()
    {
        var s = CommandParser.Parse("session 14432");
        s.Kind.Should().Be(CommandKind.Session);
        s.Index.Should().BeNull();
        s.Raw.Should().Be("14432");
    }

    [Fact]
    public void Parses_session_clear()
    {
        var s = CommandParser.Parse("session");
        s.Kind.Should().Be(CommandKind.Session);
        s.Index.Should().BeNull();
        s.Raw.Should().BeNull();
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

public class SequenceExpressionTests
{
    [Fact]
    public void Parses_mixed_ranges_and_numbers()
    {
        var expr = "514-516, 520, 522-523";
        var list = ServiceBusCli.Core.SequenceExpression.Parse(expr);
        list.Should().BeEquivalentTo(new List<long> { 514, 515, 516, 520, 522, 523 }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Handles_whitespace_and_reverse_ranges()
    {
        var expr = " 10 - 8 , 12 ";
        var list = ServiceBusCli.Core.SequenceExpression.Parse(expr);
        list.Should().BeEquivalentTo(new List<long> { 8, 9, 10, 12 }, options => options.WithStrictOrdering());
    }
}
