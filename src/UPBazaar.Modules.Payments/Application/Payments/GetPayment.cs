using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Payments.Contracts.Dtos;
using UPBazaar.Modules.Payments.Domain;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Payments.Application.Payments;

public sealed record GetPaymentQuery(Guid PaymentId) : IQuery<PaymentDto>;

internal sealed class GetPaymentQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<GetPaymentQuery, PaymentDto>
{
    public async Task<Result<PaymentDto>> HandleAsync(
        GetPaymentQuery query,
        CancellationToken cancellationToken)
    {
        var payment = await dbContext.Set<Payment>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PublicId == query.PaymentId, cancellationToken);

        return payment is null
            ? Result.Failure<PaymentDto>(PaymentErrors.NotFound)
            : payment.ToDto();
    }
}
