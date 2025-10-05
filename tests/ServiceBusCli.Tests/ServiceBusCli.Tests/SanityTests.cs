using FluentAssertions;

namespace ServiceBusCli.Tests;

public class Sanity
{
    [Fact]
    public void True_is_true()
    {
        (true).Should().BeTrue();
    }
}

