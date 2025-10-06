using FluentAssertions;
using ServiceBusCli.Core;

namespace ServiceBusCli.Tests;

public class Sanity
{
    [Fact]
    public void True_is_true()
    {
        (true).Should().BeTrue();
    }


    [Fact]
    public void Namespace_selection_uses_sorted_order()
    {
        var list = new[]
        {
            new SBNamespace("sub","rg","beta","b.servicebus.windows.net"),
            new SBNamespace("sub","rg","alpha","a.servicebus.windows.net"),
            new SBNamespace("sub","rg","gamma","g.servicebus.windows.net"),
        };
        var sel1 = SelectionHelper.ResolveNamespaceSelection(list, 1);
        sel1.Should().NotBeNull();
        sel1!.Name.Should().Be("alpha");

        var sel2 = SelectionHelper.ResolveNamespaceSelection(list, 2);
        sel2!.Name.Should().Be("beta");

        var sel3 = SelectionHelper.ResolveNamespaceSelection(list, 3);
        sel3!.Name.Should().Be("gamma");
    }

    [Fact]
    public void Entity_selection_uses_sorted_order_by_path()
    {
        var e1 = new EntityRow(EntityKind.Queue, "queueB", "Active", 10, 9, 1);
        var e2 = new EntityRow(EntityKind.Queue, "queueA", "Active", 5, 5, 0);
        var e3 = new EntityRow(EntityKind.Subscription, "topicX/Subscriptions/subY", "Active", 0, 0, 0);
        var list = new[] { e1, e2, e3 };

        var sel1 = SelectionHelper.ResolveEntitySelection(list, 1);
        sel1!.Path.Should().Be("queueA");
        var sel2 = SelectionHelper.ResolveEntitySelection(list, 2);
        sel2!.Path.Should().Be("queueB");
    }

}