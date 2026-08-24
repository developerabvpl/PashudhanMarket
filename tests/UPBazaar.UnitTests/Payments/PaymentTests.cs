using UPBazaar.Modules.Payments.Contracts.Events;
using UPBazaar.Modules.Payments.Domain;

namespace UPBazaar.UnitTests.Payments;

public sealed class PaymentTests
{
    private static readonly DateTime CapturedAt = new(2026, 3, 14, 10, 0, 0, DateTimeKind.Utc);

    private static Payment OpenPayment(decimal amount = 1000m) => Payment.Open(
        Guid.NewGuid(),
        "UPB-20260314-ABCD1234",
        Guid.NewGuid(),
        "fake",
        "order_test_0001",
        amount,
        "INR");

    [Fact]
    public void Capture_creates_a_settlement_line_net_of_commission()
    {
        var payment = OpenPayment(1000m);

        payment.Capture("pay_test_0001", CapturedAt).IsSuccess.ShouldBeTrue();

        payment.Status.ShouldBe(PaymentStatus.Captured);
        var settlement = payment.SettlementLines.ShouldHaveSingleItem();
        settlement.GrossAmount.ShouldBe(1000m);
        settlement.CommissionAmount.ShouldBe(50m);
        settlement.NetAmount.ShouldBe(950m);
        settlement.Status.ShouldBe(SettlementStatus.Pending);
    }

    [Fact]
    public void Capture_raises_the_event_ordering_listens_for()
    {
        var payment = OpenPayment();

        payment.Capture("pay_test_0001", CapturedAt);

        var captured = payment.DomainEvents.OfType<PaymentCapturedDomainEvent>().ShouldHaveSingleItem();
        captured.OrderId.ShouldBe(payment.OrderId);
        captured.Amount.ShouldBe(payment.Amount);
    }

    [Fact]
    public void Capture_of_the_same_gateway_payment_twice_does_not_settle_twice()
    {
        var payment = OpenPayment();
        payment.Capture("pay_test_0001", CapturedAt);
        payment.ClearDomainEvents();

        payment.Capture("pay_test_0001", CapturedAt).IsSuccess.ShouldBeTrue();

        payment.SettlementLines.Count.ShouldBe(1);
        payment.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void A_captured_payment_cannot_be_marked_failed()
    {
        var payment = OpenPayment();
        payment.Capture("pay_test_0001", CapturedAt);

        payment.Fail("Card declined").Error.ShouldBe(PaymentErrors.NotCapturable);
    }

    [Fact]
    public void Partial_refund_leaves_the_payment_partially_refunded()
    {
        var payment = OpenPayment(1000m);
        payment.Capture("pay_test_0001", CapturedAt);

        payment.IssueRefund("rfnd_0001", 400m, "Damaged").IsSuccess.ShouldBeTrue();

        payment.Status.ShouldBe(PaymentStatus.PartiallyRefunded);
        payment.RefundedAmount.ShouldBe(400m);
        payment.RefundableAmount.ShouldBe(600m);
    }

    [Fact]
    public void Refunding_the_full_amount_reverses_the_pending_settlement()
    {
        var payment = OpenPayment(1000m);
        payment.Capture("pay_test_0001", CapturedAt);

        payment.IssueRefund("rfnd_0001", 1000m, "Returned").IsSuccess.ShouldBeTrue();

        payment.Status.ShouldBe(PaymentStatus.Refunded);
        payment.SettlementLines.ShouldHaveSingleItem().Status.ShouldBe(SettlementStatus.Reversed);
    }

    [Fact]
    public void Refunds_cannot_exceed_the_captured_amount()
    {
        var payment = OpenPayment(1000m);
        payment.Capture("pay_test_0001", CapturedAt);
        payment.IssueRefund("rfnd_0001", 700m, "Partial");

        var second = payment.IssueRefund("rfnd_0002", 400m, "Too much");

        second.Error.ShouldBe(PaymentErrors.RefundExceedsCaptured);
        payment.RefundedAmount.ShouldBe(700m);
    }

    [Fact]
    public void An_uncaptured_payment_cannot_be_refunded()
    {
        OpenPayment().IssueRefund("rfnd_0001", 10m, "Nope").Error.ShouldBe(PaymentErrors.NotRefundable);
    }
}
