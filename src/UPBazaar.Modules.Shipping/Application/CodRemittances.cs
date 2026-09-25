using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Shipping.Application;

/// <summary>
/// Staff upload the courier's remittance report for one bank transfer: each row pays towards the
/// delivered cash-on-delivery parcel its AWB names. Rows that match no parcel yet are kept, and
/// matched when the courier reports that parcel delivered.
/// </summary>
/// <param name="Reference">The transfer's bank reference (UTR), from the bank statement or the report.</param>
/// <param name="RemittedOn">The day the money arrived.</param>
/// <param name="FileName">The file's name, to recognise it by later.</param>
/// <param name="Content">The CSV text.</param>
/// <param name="UploadedBy">Who uploaded it.</param>
public sealed record ImportCodRemittanceCommand(string Reference, DateOnly RemittedOn, string FileName, string Content, string? UploadedBy)
    : ICommand<CodRemittanceDto>;

internal sealed class ImportCodRemittanceCommandValidator : AbstractValidator<ImportCodRemittanceCommand>
{
    public ImportCodRemittanceCommandValidator()
    {
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(CodRemittance.ReferenceMaxLength);
        RuleFor(x => x.FileName).NotEmpty();
        RuleFor(x => x.Content).NotEmpty();
    }
}

internal sealed class ImportCodRemittanceCommandHandler(UPBazaarDbContext dbContext, IClock clock)
    : ICommandHandler<ImportCodRemittanceCommand, CodRemittanceDto>
{
    public async Task<Result<CodRemittanceDto>> HandleAsync(ImportCodRemittanceCommand command, CancellationToken cancellationToken)
    {
        var rows = CodRemittanceCsv.Parse(command.Content);

        if (rows.IsFailure)
        {
            return Result.Failure<CodRemittanceDto>(rows.Error);
        }

        var reference = command.Reference.Trim();

        if (await dbContext.Set<CodRemittance>().AnyAsync(r => r.Reference == reference, cancellationToken))
        {
            return Result.Failure<CodRemittanceDto>(ShippingErrors.CodReferenceTaken);
        }

        var now = clock.UtcNow;
        var remittance = CodRemittance.Create(reference, command.RemittedOn, command.FileName, rows.Value, command.UploadedBy, now);
        var awbs = remittance.Lines.Select(l => l.Awb).Distinct().ToList();
        var receivables = await dbContext.Set<CodReceivable>()
            .Where(r => awbs.Contains(r.Awb))
            .ToDictionaryAsync(r => r.Awb, cancellationToken);

        foreach (var line in remittance.Lines)
        {
            if (receivables.TryGetValue(line.Awb, out var receivable))
            {
                line.MatchTo(receivable, now);
            }
        }

        dbContext.Set<CodRemittance>().Add(remittance);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<CodRemittanceDto>(ShippingErrors.ConcurrentChange);
        }
        catch (DbUpdateException)
        {
            // Two uploads of the same report at once; the unique index on the reference let one through.
            return Result.Failure<CodRemittanceDto>(ShippingErrors.CodReferenceTaken);
        }

        return remittance.ToDto(receivables.Values.ToDictionary(r => r.PublicId, r => r.OrderNumber));
    }
}

/// <summary>
/// Staff give up on what the courier has not paid for a parcel - after raising it with the
/// courier - saying why. The cash counts as in, so the seller's earnings are released.
/// </summary>
public sealed record WriteOffCodReceivableCommand(Guid ReceivableId, string Note, string? By) : ICommand<CodReceivableDto>;

internal sealed class WriteOffCodReceivableCommandValidator : AbstractValidator<WriteOffCodReceivableCommand>
{
    public WriteOffCodReceivableCommandValidator() => RuleFor(x => x.Note).NotEmpty().MaximumLength(500);
}

internal sealed class WriteOffCodReceivableCommandHandler(UPBazaarDbContext dbContext, IClock clock, IOptions<CodOptions> options)
    : ICommandHandler<WriteOffCodReceivableCommand, CodReceivableDto>
{
    public async Task<Result<CodReceivableDto>> HandleAsync(WriteOffCodReceivableCommand command, CancellationToken cancellationToken)
    {
        var receivable = await dbContext.Set<CodReceivable>().FirstOrDefaultAsync(r => r.PublicId == command.ReceivableId, cancellationToken);

        if (receivable is null)
        {
            return Result.Failure<CodReceivableDto>(ShippingErrors.CodReceivableNotFound);
        }

        var now = clock.UtcNow;
        var written = receivable.WriteOff(command.Note, command.By, now);

        if (written.IsFailure)
        {
            return Result.Failure<CodReceivableDto>(written.Error);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<CodReceivableDto>(ShippingErrors.ConcurrentChange);
        }

        return receivable.ToDto(now, options.Value.OverdueAfter);
    }
}

/// <summary>
/// Delivered cash-on-delivery parcels, oldest delivery first, so what has been owed longest leads.
/// </summary>
/// <param name="Filter">Owed (outstanding or short; the default), Overdue, Short, Over or All.</param>
/// <param name="Page">From 1.</param>
/// <param name="PageSize">Up to 100.</param>
public sealed record ListCodReceivablesQuery(string? Filter, int Page, int PageSize) : IQuery<PagedList<CodReceivableDto>>;

internal sealed class ListCodReceivablesQueryValidator : AbstractValidator<ListCodReceivablesQuery>
{
    public static readonly string[] Filters = ["Owed", "Overdue", "Short", "Over", "All"];

    public ListCodReceivablesQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Filter)
            .Must(f => f is null || Filters.Contains(f, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Filter must be Owed, Overdue, Short, Over or All.");
    }
}

internal sealed class ListCodReceivablesQueryHandler(UPBazaarDbContext dbContext, IClock clock, IOptions<CodOptions> options)
    : IQueryHandler<ListCodReceivablesQuery, PagedList<CodReceivableDto>>
{
    public async Task<Result<PagedList<CodReceivableDto>>> HandleAsync(ListCodReceivablesQuery query, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var overdueAfter = options.Value.OverdueAfter;
        var overdueBefore = now - overdueAfter;
        var receivables = dbContext.Set<CodReceivable>().AsNoTracking();

        receivables = (query.Filter ?? "Owed").ToLowerInvariant() switch
        {
            "owed" => receivables.Where(r => r.Status == CodStatus.Outstanding || r.Status == CodStatus.ShortPaid),
            "overdue" => receivables.Where(r => (r.Status == CodStatus.Outstanding || r.Status == CodStatus.ShortPaid) && r.DeliveredAtUtc <= overdueBefore),
            "short" => receivables.Where(r => r.Status == CodStatus.ShortPaid),
            "over" => receivables.Where(r => r.Status == CodStatus.Over),
            _ => receivables,
        };

        var total = await receivables.CountAsync(cancellationToken);
        var page = await receivables
            .OrderBy(r => r.DeliveredAtUtc)
            .ThenBy(r => r.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<CodReceivableDto>([.. page.Select(r => r.ToDto(now, overdueAfter))], query.Page, query.PageSize, total);
    }
}

/// <summary>What the courier owes, at a glance.</summary>
public sealed record GetCodSummaryQuery : IQuery<CodSummaryDto>;

internal sealed class GetCodSummaryQueryHandler(UPBazaarDbContext dbContext, IClock clock, IOptions<CodOptions> options)
    : IQueryHandler<GetCodSummaryQuery, CodSummaryDto>
{
    public async Task<Result<CodSummaryDto>> HandleAsync(GetCodSummaryQuery query, CancellationToken cancellationToken)
    {
        var overdueBefore = clock.UtcNow - options.Value.OverdueAfter;
        var owed = await dbContext.Set<CodReceivable>()
            .AsNoTracking()
            .Where(r => r.Status == CodStatus.Outstanding || r.Status == CodStatus.ShortPaid)
            .Select(r => new { r.Expected, r.Received, r.Status, r.DeliveredAtUtc })
            .ToListAsync(cancellationToken);

        return new CodSummaryDto(
            owed.Sum(r => r.Expected - r.Received),
            owed.Count,
            owed.Count(r => r.DeliveredAtUtc <= overdueBefore),
            owed.Count(r => r.Status == CodStatus.ShortPaid),
            options.Value.OverdueDays);
    }
}

/// <summary>Uploaded remittances, newest first. The rows of each are left out; ask for one to see them.</summary>
public sealed record ListCodRemittancesQuery(int Page, int PageSize) : IQuery<PagedList<CodRemittanceDto>>;

internal sealed class ListCodRemittancesQueryValidator : AbstractValidator<ListCodRemittancesQuery>
{
    public ListCodRemittancesQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

internal sealed class ListCodRemittancesQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<ListCodRemittancesQuery, PagedList<CodRemittanceDto>>
{
    public async Task<Result<PagedList<CodRemittanceDto>>> HandleAsync(ListCodRemittancesQuery query, CancellationToken cancellationToken)
    {
        var remittances = dbContext.Set<CodRemittance>().AsNoTracking();
        var total = await remittances.CountAsync(cancellationToken);
        var page = await remittances
            .OrderByDescending(r => r.UploadedAtUtc)
            .ThenByDescending(r => r.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(r => new CodRemittanceDto(
                r.PublicId,
                r.Reference,
                r.RemittedOn,
                r.Lines.Sum(l => l.Amount),
                r.Lines.Count(l => l.ReceivableId == null),
                r.FileName,
                r.UploadedAtUtc,
                r.UploadedBy,
                Array.Empty<CodRemittanceLineDto>()))
            .ToListAsync(cancellationToken);

        return new PagedList<CodRemittanceDto>(page, query.Page, query.PageSize, total);
    }
}

/// <summary>One remittance with every row, and the order each matched row paid for.</summary>
public sealed record GetCodRemittanceQuery(Guid RemittanceId) : IQuery<CodRemittanceDto>;

internal sealed class GetCodRemittanceQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<GetCodRemittanceQuery, CodRemittanceDto>
{
    public async Task<Result<CodRemittanceDto>> HandleAsync(GetCodRemittanceQuery query, CancellationToken cancellationToken)
    {
        var remittance = await dbContext.Set<CodRemittance>()
            .AsNoTracking()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.PublicId == query.RemittanceId, cancellationToken);

        if (remittance is null)
        {
            return Result.Failure<CodRemittanceDto>(ShippingErrors.CodRemittanceNotFound);
        }

        var matched = remittance.Lines.Where(l => l.ReceivableId != null).Select(l => l.ReceivableId!.Value).Distinct().ToList();
        var orders = await dbContext.Set<CodReceivable>()
            .AsNoTracking()
            .Where(r => matched.Contains(r.PublicId))
            .ToDictionaryAsync(r => r.PublicId, r => r.OrderNumber, cancellationToken);

        return remittance.ToDto(orders);
    }
}

/// <summary>Maps cash-on-delivery records to the DTOs the API shows.</summary>
internal static class CodMappings
{
    public static CodReceivableDto ToDto(this CodReceivable receivable, DateTime now, TimeSpan overdueAfter) => new(
        receivable.PublicId,
        receivable.OrderId,
        receivable.OrderNumber,
        receivable.OrderPartId,
        receivable.SellerId,
        receivable.Awb,
        receivable.Expected,
        receivable.Received,
        receivable.Status.ToString(),
        receivable.DeliveredAtUtc,
        receivable.IsOverdue(now, overdueAfter),
        receivable.WriteOffNote);

    /// <param name="remittance">The remittance, with its lines.</param>
    /// <param name="orderNumbers">The order number of each receivable a line matched, by its public id.</param>
    public static CodRemittanceDto ToDto(this CodRemittance remittance, IReadOnlyDictionary<Guid, string> orderNumbers) => new(
        remittance.PublicId,
        remittance.Reference,
        remittance.RemittedOn,
        remittance.Total,
        remittance.Lines.Count(l => l.ReceivableId is null),
        remittance.FileName,
        remittance.UploadedAtUtc,
        remittance.UploadedBy,
        [.. remittance.Lines.OrderBy(l => l.Id).Select(l => new CodRemittanceLineDto(
            l.Awb,
            l.Amount,
            l.ReceivableId is not null,
            l.ReceivableId is { } id ? orderNumbers.GetValueOrDefault(id) : null))]);
}
