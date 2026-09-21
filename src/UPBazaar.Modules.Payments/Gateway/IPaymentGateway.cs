using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Payments.Gateway;

/// <summary>
/// The payment provider, as far as this module needs it: create an order to pay against, and
/// check the signatures on what comes back.
///
/// An interface so there can be three of them. <see cref="RazorpayGateway"/> talks to Razorpay;
/// <see cref="FakeGateway"/> stands in for it in development and tests, where there are no keys and
/// no way for Razorpay to reach a laptop; <see cref="UnconfiguredGateway"/> is what a server without
/// keys gets, so online payment is switched off rather than half-working.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Razorpay, Fake, or None.</summary>
    string Name { get; }

    /// <summary>False only for the unconfigured gateway.</summary>
    bool IsEnabled { get; }

    /// <summary>The public key id the browser needs. Empty for the fake.</summary>
    string KeyId { get; }

    /// <summary>Creates the gateway-side order the buyer pays against. Returns its id.</summary>
    /// <param name="amountInPaise">Amount in paise.</param>
    /// <param name="currency">ISO currency code.</param>
    /// <param name="receipt">Our reference, shown in the Razorpay dashboard: the order number.</param>
    /// <param name="orderId">Our order id, kept in the gateway order's notes for tracing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<string>> CreateOrderAsync(
        long amountInPaise,
        string currency,
        string receipt,
        Guid orderId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks the signature Checkout hands the browser after a successful payment. Without it, any
    /// buyer could post a made-up payment id and have their order confirmed.
    /// </summary>
    bool IsPaymentSignatureValid(string gatewayOrderId, string gatewayPaymentId, string signature);

    /// <summary>Checks the signature on a webhook delivery, over the exact bytes received.</summary>
    bool IsWebhookSignatureValid(string rawBody, string signature);
}
