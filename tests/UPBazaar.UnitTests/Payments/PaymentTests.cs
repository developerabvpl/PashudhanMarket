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

        payment.RecordPartRefundDue(part, 300m, "Cancelled", Now).IsSuccess.ShouldBeTrue();
        payment.RecordPartRefundDue(part, 300m, "Cancelled", Now).IsSuccess.ShouldBeTrue();
        payment.RecordPartRefundDue(Guid.NewGuid(), 201m, "Cancelled", Now).Error
            .ShouldBe(PaymentErrors.RefundExceedsPayment);

        payment.RefundDue.ShouldBe(300m);
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
