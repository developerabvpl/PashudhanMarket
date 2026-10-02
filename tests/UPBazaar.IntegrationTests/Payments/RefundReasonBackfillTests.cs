using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Infrastructure.Persistence.Migrations;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Payments.Domain;

namespace UPBazaar.IntegrationTests.Payments;

/// <summary>
/// The RefundReasonCodes migration gave refunds recorded before codes existed a code from their
/// sentence. The fixture's database was empty when it ran, so the backfill is run again here over
/// rows written the old way - text only - to prove each sentence lands on the right code and
/// anything else is left for the portal to show as text.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RefundReasonBackfillTests(ApiFixture fixture)
{
    [DatabaseFact]
    public async Task Old_refunds_get_the_code_their_sentence_was_written_for_and_others_stay_null()
    {
        var prefix = $"BF{Guid.NewGuid():N}"[..12];

        (string Number, string Reason, RefundReason? Expected)[] rows =
        [
            ($"{prefix}-1", "The order was cancelled.", RefundReason.OrderCancelled),
            ($"{prefix}-2", "Part of the order was cancelled.", RefundReason.PartCancelled),
            ($"{prefix}-3", "The parcel could not be delivered and went back to the seller.", RefundReason.Undelivered),
            ($"{prefix}-4", "The buyer returned the parcel and it is back with the seller.", RefundReason.BuyerReturn),
            ($"{prefix}-5", "Payment could not be applied to the order: This order is not waiting for payment.", RefundReason.PaymentRefused),
            ($"{prefix}-6", "Refunded by hand after a phone call.", null),
        ];

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        foreach (var (number, reason, _) in rows)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO payments.Refunds (PublicId, OrderId, OrderNumber, Amount, Currency, Method, Reason, Status, CreatedAtUtc)
                VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, {number}, 10, 'INR', 'Razorpay', {reason}, 'Due', SYSUTCDATETIME())
                """);
        }

        await dbContext.Database.ExecuteSqlRawAsync(RefundReasonCodes.BackfillSql);

        var codes = await dbContext.Set<Refund>()
            .AsNoTracking()
            .Where(r => r.OrderNumber.StartsWith(prefix))
            .ToDictionaryAsync(r => r.OrderNumber, r => r.ReasonCode);

        foreach (var (number, _, expected) in rows)
        {
            codes[number].ShouldBe(expected, number);
        }
    }
}
