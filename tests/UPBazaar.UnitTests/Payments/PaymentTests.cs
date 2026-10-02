using UPBazaar.Modules.Payments.Domain;
using UPBazaar.Modules.Payments.Gateway;

namespace UPBazaar.UnitTests.Payments;

public sealed class PaymentTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Reporting_the_same_gateway_payment_twice_is_harmless_but_a_different_one_is_refused()
    {
        var payment = NewPayment();

        payment.MarkPaid("pay_1", Now).IsSuccess.ShouldBeTrue();
        payment.MarkPaid("pay_1", Now).IsSuccess.ShouldBeTrue();
        payment.MarkPaid("pay_2", Now).Error.ShouldBe(PaymentErrors.PaidWithAnotherPayment);

        payment.GatewayPaymentId.ShouldBe("pay_1");
    }

    [Fact]
    public void A_refused_payment_is_owed_back_in_full_once()
    {
        var payment = NewPayment();
        payment.MarkPaid("pay_1", Now);

        payment.RecordOrderRefused("Order was cancelled.", Now);
        payment.RecordOrderRefused("Order was cancelled.", Now);

        payment.OrderOutcome.ShouldBe(OrderOutcome.Refused);
        payment.Refunds.ShouldHaveSingleItem().Amount.ShouldBe(500m);
        payment.RefundDue.ShouldBe(500m);
    }

    [Fact]
    public void Part_refunds_are_recorded_once_per_part_and_never_exceed_the_payment()
    {
        var payment = NewPayment();
        payment.MarkPaid("pay_1", Now);
        payment.RecordOrderConfirmed();
        var part = Guid.NewGuid();

        payment.RecordPartRefundDue(part, 300m, RefundReason.PartCancelled, Now).IsSuccess.ShouldBeTrue();
        payment.RecordPartRefundDue(part, 300m, RefundReason.PartCancelled, Now).IsSuccess.ShouldBeTrue();
        payment.RecordPartRefundDue(Guid.NewGuid(), 201m, RefundReason.PartCancelled, Now).Error
            .ShouldBe(PaymentErrors.RefundExceedsPayment);

        payment.RefundDue.ShouldBe(300m);
    }

    [Fact]
    public void A_confirmed_payment_whose_order_is_cancelled_says_so_and_still_takes_its_part_refunds()
    {
        var payment = NewPayment();
        payment.MarkPaid("pay_1", Now);
        payment.RecordOrderConfirmed();

        payment.RecordOrderCancelled();

        payment.OrderOutcome.ShouldBe(OrderOutcome.Cancelled);
        payment.RecordPartRefundDue(Guid.NewGuid(), 500m, RefundReason.OrderCancelled, Now).IsSuccess.ShouldBeTrue();
        payment.RefundDue.ShouldBe(500m);
    }

    [Fact]
    public void Only_a_confirmed_payment_is_marked_cancelled()
    {
        // Pending: it will be refused, and owed back whole, when it reaches the cancelled order.
        var pending = NewPayment();
        pending.MarkPaid("pay_1", Now);
        pending.RecordOrderCancelled();
        pending.OrderOutcome.ShouldBe(OrderOutcome.Pending);

        var refused = NewPayment();
        refused.MarkPaid("pay_2", Now);
        refused.RecordOrderRefused("Order was cancelled.", Now);
        refused.RecordOrderCancelled();
        refused.OrderOutcome.ShouldBe(OrderOutcome.Refused);
    }

    [Fact]
    public void A_refund_is_marked_made_only_once()
    {
        var payment = NewPayment();
        payment.MarkPaid("pay_1", Now);
        payment.RecordOrderRefused("Too late", Now);
        var refund = payment.Refunds.Single();

        refund.MarkRefunded(" rfnd_1 ", "staff-1", Now).IsSuccess.ShouldBeTrue();
        refund.MarkRefunded("rfnd_2", "staff-1", Now).Error.ShouldBe(PaymentErrors.AlreadyRefunded);

        refund.GatewayRefundId.ShouldBe("rfnd_1");
        payment.RefundDue.ShouldBe(0m);
    }

    [Fact]
    public void A_refused_payment_is_owed_back_with_its_code_and_what_orders_said()
    {
        var payment = NewPayment();
        payment.MarkPaid("pay_1", Now);

        payment.RecordOrderRefused("The payment deadline has passed.", Now);

        var refund = payment.Refunds.ShouldHaveSingleItem();
        refund.ReasonCode.ShouldBe(RefundReason.PaymentRefused);
        refund.Reason.ShouldBe("Payment could not be applied to the order: The payment deadline has passed.");
    }

    [Theory]
    [InlineData(RefundReason.OrderCancelled, "The order was cancelled.")]
    [InlineData(RefundReason.PartCancelled, "Part of the order was cancelled.")]
    [InlineData(RefundReason.Undelivered, "The parcel could not be delivered and went back to the seller.")]
    [InlineData(RefundReason.BuyerReturn, "The buyer returned the parcel and it is back with the seller.")]
    public void A_part_refund_keeps_its_code_beside_the_sentence_it_always_wrote(RefundReason code, string sentence)
    {
        var payment = NewPayment();
        payment.MarkPaid("pay_1", Now);
        payment.RecordOrderConfirmed();

        payment.RecordPartRefundDue(Guid.NewGuid(), 100m, code, Now).IsSuccess.ShouldBeTrue();

        var refund = payment.Refunds.ShouldHaveSingleItem();
        refund.ReasonCode.ShouldBe(code);

        // The audit text must not change: the backfill migration matched old rows on these sentences.
        refund.Reason.ShouldBe(sentence);
    }

    [Fact]
    public void A_upi_refund_carries_the_buyer_return_code()
    {
        var refund = Refund.ToUpi(
            Guid.NewGuid(), "UPB-260922-ABCDEF", Guid.NewGuid(), 150m, "INR", " asha@okicici ", RefundReason.BuyerReturn, Now);

        refund.ReasonCode.ShouldBe(RefundReason.BuyerReturn);
        refund.Reason.ShouldBe("The buyer returned the parcel and it is back with the seller.");
        refund.UpiId.ShouldBe("asha@okicici");
    }

    [Fact]
    public void A_failed_attempt_is_noted_only_while_the_payment_is_open()
    {
        var payment = NewPayment();

        payment.RecordFailedAttempt("Card declined");
        payment.LastFailure.ShouldBe("Card declined");

        payment.MarkPaid("pay_1", Now);
        payment.LastFailure.ShouldBeNull();

        payment.RecordFailedAttempt("Late failure");
        payment.LastFailure.ShouldBeNull();
    }

    private static Payment NewPayment() =>
        Payment.Create(Guid.NewGuid(), "UPB-260922-ABCDEF", Guid.NewGuid(), 500m, "INR", "Fake", "order_1", Now.AddMinutes(15));
}

public sealed class RazorpaySignatureTests
{
    [Fact]
    public void A_payment_signature_is_the_hex_hmac_of_order_and_payment_ids()
    {
        // Worked out independently: HMAC-SHA256("order_1|pay_1", "secret"), lower-case hex.
        var expected = Convert.ToHexStringLower(
            System.Security.Cryptography.HMACSHA256.HashData("secret"u8, "order_1|pay_1"u8));

        RazorpaySignature.ForPayment("order_1", "pay_1", "secret").ShouldBe(expected);
    }

    [Fact]
    public void Matching_ignores_case_and_surrounding_space_but_nothing_else()
    {
        var signature = RazorpaySignature.ForWebhook("{\"event\":\"payment.captured\"}", "secret");

        RazorpaySignature.Matches(signature, $"  {signature.ToUpperInvariant()} ").ShouldBeTrue();
        var tampered = signature[..^1] + (signature[^1] == 'a' ? 'b' : 'a');
        RazorpaySignature.Matches(signature, tampered).ShouldBeFalse();
        RazorpaySignature.Matches(signature, null).ShouldBeFalse();
        RazorpaySignature.Matches(signature, "").ShouldBeFalse();
    }

    [Fact]
    public void The_fake_gateway_rejects_a_signature_made_with_another_secret()
    {
        var gateway = new FakeGateway();

        gateway.IsPaymentSignatureValid("order_1", "pay_1", RazorpaySignature.ForPayment("order_1", "pay_1", FakeGateway.KeySecret))
            .ShouldBeTrue();
        gateway.IsPaymentSignatureValid("order_1", "pay_1", RazorpaySignature.ForPayment("order_1", "pay_1", "guess"))
            .ShouldBeFalse();
        gateway.IsPaymentSignatureValid("order_1", "pay_2", RazorpaySignature.ForPayment("order_1", "pay_1", FakeGateway.KeySecret))
            .ShouldBeFalse();
    }
}
