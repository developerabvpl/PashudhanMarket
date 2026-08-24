using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.UnitTests.SharedKernel;

/// <summary>A stand-in aggregate, so the base class is tested without waiting for a real one.</summary>
internal sealed class TestAggregate : AggregateRoot
{
    public void DoSomething() => Raise(new SomethingHappened(PublicId));

    internal sealed record SomethingHappened(Guid AggregateId) : DomainEvent;
}

public sealed class AggregateRootTests
{
    [Fact]
    public void A_new_aggregate_has_a_sortable_public_id()
    {
        var first = new TestAggregate();
        var second = new TestAggregate();

        first.PublicId.ShouldNotBe(Guid.Empty);

        // Version 7 ids embed a timestamp, so later ids sort after earlier ones. That is what
        // keeps them from fragmenting an index the way random GUIDs do.
        first.PublicId.CompareTo(second.PublicId).ShouldBeLessThan(0);
    }

    [Fact]
    public void An_aggregate_starts_with_no_events()
    {
        new TestAggregate().DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Raising_an_event_records_it()
    {
        var aggregate = new TestAggregate();

        aggregate.DoSomething();

        var raised = aggregate.DomainEvents.ShouldHaveSingleItem();
        raised.ShouldBeOfType<TestAggregate.SomethingHappened>()
            .AggregateId.ShouldBe(aggregate.PublicId);
    }

    [Fact]
    public void Every_event_gets_its_own_identity_and_timestamp()
    {
        var aggregate = new TestAggregate();

        aggregate.DoSomething();
        aggregate.DoSomething();

        var events = aggregate.DomainEvents.ToList();

        events.Count.ShouldBe(2);
        events[0].EventId.ShouldNotBe(events[1].EventId);
        events[0].OccurredAtUtc.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public void Clearing_events_leaves_the_aggregate_otherwise_intact()
    {
        var aggregate = new TestAggregate();
        var id = aggregate.PublicId;
        aggregate.DoSomething();

        aggregate.ClearDomainEvents();

        aggregate.DomainEvents.ShouldBeEmpty();
        aggregate.PublicId.ShouldBe(id);
    }
}
