using UPBazaar.Modules.Shipping.Domain;

namespace UPBazaar.UnitTests.Shipping;

public sealed class ShipmentTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("PICKED UP", ShipmentStatus.InTransit)]
    [InlineData("In Transit", ShipmentStatus.InTransit)]
    [InlineData("OUT FOR DELIVERY", ShipmentStatus.InTransit)]
    [InlineData(" delivered ", ShipmentStatus.Delivered)]
    [InlineData("RTO INITIATED", ShipmentStatus.ReturnInTransit)]
    [InlineData("RTO IN TRANSIT", ShipmentStatus.ReturnInTransit)]
    [InlineData("RTO DELIVERED", ShipmentStatus.Returned)]
    [InlineData("CANCELED", ShipmentStatus.Cancelled)]
    public void Courier_wording_maps_to_a_shipment_status(string raw, ShipmentStatus expected) =>
        CourierStatus.Map(raw).ShouldBe(expected);

    [Theory]
    [InlineData("AWB ASSIGNED")]
    [InlineData("PICKUP SCHEDULED")]
    [InlineData("OUT FOR PICKUP")]
    public void Updates_before_collection_move_nothing(string raw) => CourierStatus.Map(raw).ShouldBeNull();

    [Fact]
    public void A_shipment_only_moves_forward_but_records_every_update()
    {
        var shipment = Booked();

        shipment.ApplyCourierStatus("IN TRANSIT", ShipmentStatus.InTransit, Now).ShouldBeTrue();
        shipment.ApplyCourierStatus("DELIVERED", ShipmentStatus.Delivered, Now).ShouldBeTrue();
        shipment.ApplyCourierStatus("IN TRANSIT", ShipmentStatus.InTransit, Now).ShouldBeFalse();

        shipment.Status.ShouldBe(ShipmentStatus.Delivered);
        shipment.Events.Count.ShouldBe(3);
    }

    [Fact]
    public void Nothing_moves_a_returned_shipment()
    {
        var shipment = Booked();
        shipment.ApplyCourierStatus("RTO INITIATED", ShipmentStatus.Returned, Now);

        shipment.ApplyCourierStatus("DELIVERED", ShipmentStatus.Delivered, Now).ShouldBeFalse();
        shipment.Status.ShouldBe(ShipmentStatus.Returned);
    }

    [Fact]
    public void A_return_runs_from_in_transit_to_returned_and_never_turns_into_a_delivery()
    {
        var shipment = Booked();
        shipment.ApplyCourierStatus("IN TRANSIT", ShipmentStatus.InTransit, Now);

        shipment.ApplyCourierStatus("RTO INITIATED", ShipmentStatus.ReturnInTransit, Now).ShouldBeTrue();
        shipment.ApplyCourierStatus("DELIVERED", ShipmentStatus.Delivered, Now).ShouldBeFalse();
        shipment.ApplyCourierStatus("RTO DELIVERED", ShipmentStatus.Returned, Now).ShouldBeTrue();

        shipment.Status.ShouldBe(ShipmentStatus.Returned);
    }

    [Fact]
    public void A_delivered_shipment_is_not_turned_round_by_a_late_rto()
    {
        var shipment = Booked();
        shipment.ApplyCourierStatus("DELIVERED", ShipmentStatus.Delivered, Now);

        shipment.ApplyCourierStatus("RTO INITIATED", ShipmentStatus.ReturnInTransit, Now).ShouldBeFalse();
        shipment.Status.ShouldBe(ShipmentStatus.Delivered);
    }

    [Fact]
    public void Cash_on_delivery_is_booked_as_cod_and_the_reference_is_unique_per_part()
    {
        var part = Guid.NewGuid();
        var cod = Shipment.Create(Guid.NewGuid(), "UPB-260922-ABCDEF", part, Guid.NewGuid(), Guid.NewGuid(), "Fake", "Warehouse", (500, 20m, 15m, 10m), 417m);
        var prepaid = Shipment.Create(Guid.NewGuid(), "UPB-260922-ABCDEF", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Fake", "Warehouse", (500, 20m, 15m, 10m), 0m);

        cod.PaymentMode.ShouldBe("COD");
        prepaid.PaymentMode.ShouldBe("Prepaid");
        cod.CarrierReference.ShouldStartWith("UPB-260922-ABCDEF-");
        cod.CarrierReference.ShouldNotBe(prepaid.CarrierReference);
    }

    [Fact]
    public void Only_a_shipment_not_yet_collected_can_be_cancelled()
    {
        var shipment = Booked();
        shipment.CanCancel.ShouldBeTrue();

        shipment.ApplyCourierStatus("PICKED UP", ShipmentStatus.InTransit, Now);

        shipment.CanCancel.ShouldBeFalse();
    }

    private static Shipment Booked()
    {
        var shipment = Shipment.Create(
            Guid.NewGuid(), "UPB-260922-ABCDEF", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Fake", "Warehouse", (500, 20m, 15m, 10m), 0m);

        shipment.RecordCarrierOrder("1", "2");
        shipment.RecordAwb("AWB1", "Fake Express");
        shipment.RecordPickupRequested(Now);

        return shipment;
    }
}
