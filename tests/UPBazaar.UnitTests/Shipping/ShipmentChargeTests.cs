using UPBazaar.Modules.Shipping.Contracts.Events;
using UPBazaar.Modules.Shipping.Domain;

namespace UPBazaar.UnitTests.Shipping;

/// <summary>
/// Courier charges: quoted at booking, charged when the trip happens, and corrected by the
/// difference.
/// </summary>
public sealed class ShipmentChargeTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Nothing_is_charged_until_the_courier_has_the_parcel()
    {
        var shipment = Forward(codAmount: 199m);
        shipment.RecordQuote("1", freight: 60m, codCharge: 30m);

        shipment.ApplyCourierStatus("PICKUP SCHEDULED", null, Now);
        Charged(shipment).ShouldBeEmpty();

        shipment.ApplyCourierStatus("PICKED UP", ShipmentStatus.InTransit, Now);
        var charged = Charged(shipment).ShouldHaveSingleItem();
        charged.Trip.ShouldBe("Delivery");
        charged.Amount.ShouldBe(90m);
        charged.Sequence.ShouldBe(1);

        // Later scans charge nothing more.
        shipment.ClearDomainEvents();
        shipment.ApplyCourierStatus("DELIVERED", ShipmentStatus.Delivered, Now);
        Charged(shipment).ShouldBeEmpty();
    }

    [Fact]
    public void A_booking_cancelled_before_collection_costs_nothing()
    {
        var shipment = Forward(codAmount: 0m);
        shipment.RecordQuote("1", freight: 60m, codCharge: 0m);

        shipment.ApplyCourierStatus("CANCELLED", ShipmentStatus.Cancelled, Now);

        Charged(shipment).ShouldBeEmpty();
    }

    [Fact]
    public void An_rto_is_charged_at_the_forward_freight()
    {
        var shipment = Forward(codAmount: 199m);
        shipment.RecordQuote("1", freight: 60m, codCharge: 30m);
        shipment.ApplyCourierStatus("PICKED UP", ShipmentStatus.InTransit, Now);
        shipment.ClearDomainEvents();

        shipment.ApplyCourierStatus("RTO INITIATED", ShipmentStatus.ReturnInTransit, Now);
        shipment.ApplyCourierStatus("RTO DELIVERED", ShipmentStatus.Returned, Now);

        var rto = Charged(shipment).ShouldHaveSingleItem();
        rto.Trip.ShouldBe("Rto");
        rto.Amount.ShouldBe(60m);
    }

    [Fact]
    public void A_parcel_that_could_not_be_priced_is_charged_when_staff_enter_the_figure()
    {
        var shipment = Forward(codAmount: 0m);
        shipment.RecordQuoteError("No PIN code.");
        shipment.ApplyCourierStatus("PICKED UP", ShipmentStatus.InTransit, Now);
        Charged(shipment).ShouldBeEmpty();

        shipment.CorrectCharge(CourierTrip.Delivery, 75m, "staff", "Invoice 1234", Now).IsSuccess.ShouldBeTrue();

        Charged(shipment).ShouldHaveSingleItem().Amount.ShouldBe(75m);
    }

    [Fact]
    public void A_correction_charges_or_gives_back_only_the_difference()
    {
        var shipment = Forward(codAmount: 0m);
        shipment.RecordQuote("1", freight: 60m, codCharge: 0m);
        shipment.ApplyCourierStatus("PICKED UP", ShipmentStatus.InTransit, Now);
        shipment.ClearDomainEvents();

        shipment.CorrectCharge(CourierTrip.Delivery, 80m, "staff", "Re-weighed", Now);
        shipment.CorrectCharge(CourierTrip.Delivery, 70m, "staff", "Dispute won", Now);
        shipment.CorrectCharge(CourierTrip.Delivery, 70m, "staff", null, Now);

        var charges = Charged(shipment);
        charges.Select(c => c.Amount).ShouldBe([20m, -10m]);
        charges.Select(c => c.Sequence).ShouldBe([2, 3]);
    }

    [Fact]
    public void A_correction_before_the_trip_is_what_gets_charged_when_it_happens()
    {
        var shipment = Forward(codAmount: 0m);
        shipment.RecordQuote("1", freight: 60m, codCharge: 0m);
        shipment.CorrectCharge(CourierTrip.Delivery, 65m, "staff", null, Now);
        Charged(shipment).ShouldBeEmpty();

        shipment.ApplyCourierStatus("PICKED UP", ShipmentStatus.InTransit, Now);

        Charged(shipment).ShouldHaveSingleItem().Amount.ShouldBe(65m);
    }

    [Fact]
    public void Each_direction_is_charged_only_for_its_own_trips()
    {
        Forward(codAmount: 0m).CorrectCharge(CourierTrip.ReturnPickup, 50m, null, null, Now).Error
            .ShouldBe(ShippingErrors.TripNotOnShipment);
        Return("Damaged").CorrectCharge(CourierTrip.Rto, 50m, null, null, Now).Error
            .ShouldBe(ShippingErrors.TripNotOnShipment);
    }

    [Fact]
    public void A_return_pickup_is_charged_at_its_freight_and_carries_the_buyer_s_reason()
    {
        var shipment = Return("NoLongerNeeded");
        shipment.RecordQuote("1", freight: 55m, codCharge: 0m);

        shipment.ApplyCourierStatus("RETURN PICKED UP", ShipmentStatus.InTransit, Now);

        var charged = Charged(shipment).ShouldHaveSingleItem();
        charged.Trip.ShouldBe("ReturnPickup");
        charged.Amount.ShouldBe(55m);
        charged.ReturnReason.ShouldBe("NoLongerNeeded");
    }

    private static List<ShipmentChargedDomainEvent> Charged(Shipment shipment) =>
        [.. shipment.DomainEvents.OfType<ShipmentChargedDomainEvent>()];

    private static Shipment Forward(decimal codAmount) =>
        Shipment.Create(Guid.NewGuid(), "UPB-260925-ABCDEF", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Fake", "Warehouse", (500, 20m, 15m, 10m), codAmount);

    private static Shipment Return(string reason) =>
        Shipment.CreateReturn(Guid.NewGuid(), "UPB-260925-ABCDEF", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Fake", (500, 20m, 15m, 10m), reason);
}
