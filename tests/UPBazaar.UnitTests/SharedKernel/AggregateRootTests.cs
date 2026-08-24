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
    public void A_new_aggregate_has_a_time_ordered_public_id()
    {
        var first = new TestAggregate();
        var second = new TestAggregate();

        first.PublicId.ShouldNotBe(Guid.Empty);
        first.PublicId.ShouldNotBe(second.PublicId);

        // Version 7 puts a 48-bit big-endian timestamp in the leading bytes, which is what
        // gives an index on this column locality. Note this is a property of the byte layout,
        // not of Guid.CompareTo, which compares structurally and does not preserve the order.
        TimestampOf(second.PublicId).ShouldBeGreaterThanOrEqualTo(TimestampOf(first.PublicId));
    }

    [Fact]
    public void The_public_id_declares_itself_as_version_7()
    {
        var bytes = new TestAggregate().PublicId.ToByteArray(bigEndian: true);

        // Byte 6, high nibble, is the version field.
        var version = bytes[6] >> 4;

        version.ShouldBe(7);
    }

    private static long TimestampOf(Guid id)
    {
        var bytes = id.ToByteArray(bigEndian: true);

        return ((long)bytes[0] << 40)
            | ((long)bytes[1] << 32)
            | ((long)bytes[2] << 24)
            | ((long)bytes[3] << 16)
            | ((long)bytes[4] << 8)
            | bytes[5];
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
