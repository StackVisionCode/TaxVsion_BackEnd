using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Application.Feedback;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Repositories;

public sealed class SentMessageRepository(PostmasterDbContext dbContext) : ISentMessageRepository
{
    public async Task AddAsync(SentMessage message, CancellationToken ct = default) =>
        await dbContext.SentMessages.AddAsync(message, ct);

    public void Remove(SentMessage message) => dbContext.SentMessages.Remove(message);

    public async Task<Result<SentMessage>> GetByIdWithEventsAsync(
        Guid tenantId,
        Guid id,
        CancellationToken ct = default
    )
    {
        var message = await dbContext
            .SentMessages.IgnoreQueryFilters()
            .Include(m => m.Events)
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenantId, ct);

        return message is null
            ? Result.Failure<SentMessage>(new Error("SentMessage.NotFound", $"SentMessage {id} not found."))
            : Result.Success(message);
    }

    public async Task<(IReadOnlyList<SentMessage> Items, int Total)> ListForActivityAsync(
        Guid tenantId,
        SentMessageActivityFilter filter,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var query = ApplyActivityFilter(
            dbContext.SentMessages.IgnoreQueryFilters().Where(m => m.TenantId == tenantId),
            filter
        );

        var total = await query.CountAsync(ct);
        var items = await query
            .Include(m => m.Recipients)
            .OrderByDescending(m => m.QueuedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .AsNoTracking()
            .AsSplitQuery()
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<Result<SentMessage>> GetDetailAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var message = await dbContext
            .SentMessages.IgnoreQueryFilters()
            .Include(m => m.Recipients)
            .Include(m => m.Events)
            .AsNoTracking()
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenantId, ct);

        return message is null
            ? Result.Failure<SentMessage>(new Error("SentMessage.NotFound", $"SentMessage {id} not found."))
            : Result.Success(message);
    }

    /// <summary>Los mismos estados que muestra la pantalla (ver SentMessageActivityStatus).</summary>
    private static IQueryable<SentMessage> ApplyActivityFilter(
        IQueryable<SentMessage> query,
        SentMessageActivityFilter filter
    )
    {
        if (filter.FromUtc is { } from)
            query = query.Where(m => m.QueuedAtUtc >= from);
        if (filter.ToUtc is { } to)
            query = query.Where(m => m.QueuedAtUtc < to);
        if (!string.IsNullOrWhiteSpace(filter.Recipient))
        {
            var recipient = filter.Recipient.Trim().ToLowerInvariant();
            query = query.Where(m => m.Recipients.Any(r => r.Address.Contains(recipient)));
        }

        return filter.Status switch
        {
            SentMessageActivityStatus.Delivered => query.Where(m =>
                m.Recipients.Any(r => r.Outcome == DeliveryOutcome.Delivered)
            ),
            SentMessageActivityStatus.Bounced => query.Where(m =>
                m.Recipients.Any(r =>
                    r.Outcome == DeliveryOutcome.HardBounced
                    || r.Outcome == DeliveryOutcome.SoftBounced
                    || r.Outcome == DeliveryOutcome.Rejected
                )
            ),
            SentMessageActivityStatus.Complained => query.Where(m =>
                m.Recipients.Any(r => r.Outcome == DeliveryOutcome.Complained)
            ),
            SentMessageActivityStatus.Sent => query.Where(m =>
                m.Status == SentMessageStatus.Sent
                && m.Recipients.Any(r => r.Outcome == DeliveryOutcome.Accepted || r.Outcome == DeliveryOutcome.Deferred)
            ),
            SentMessageActivityStatus.Failed => query.Where(m =>
                m.Status == SentMessageStatus.Failed || m.Status == SentMessageStatus.ProviderNotConfigured
            ),
            SentMessageActivityStatus.Suppressed => query.Where(m => m.Status == SentMessageStatus.Suppressed),
            _ => query,
        };
    }

    public async Task<Result<SentMessage>> GetByIdForFeedbackAsync(Guid id, CancellationToken ct = default)
    {
        var message = await dbContext
            .SentMessages.IgnoreQueryFilters()
            .Include(m => m.Recipients)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        return message is null
            ? Result.Failure<SentMessage>(new Error("SentMessage.NotFound", $"SentMessage {id} not found."))
            : Result.Success(message);
    }

    public async Task<IReadOnlyList<PendingDeliveryRef>> ListPendingDeliveryAsync(
        DateTime sentFromUtc,
        DateTime sentToUtc,
        int max,
        CancellationToken ct = default
    ) =>
        await (
            from recipient in dbContext.SentMessageRecipients.IgnoreQueryFilters()
            join message in dbContext.SentMessages.IgnoreQueryFilters() on recipient.SentMessageId equals message.Id
            where
                message.DeliveryLane == DeliveryLane.SystemProvider
                && message.SentAtUtc >= sentFromUtc
                && message.SentAtUtc < sentToUtc
                && (recipient.Outcome == DeliveryOutcome.Accepted || recipient.Outcome == DeliveryOutcome.Deferred)
                && recipient.ProviderMessageId != null
            orderby message.SentAtUtc
            select new PendingDeliveryRef(
                message.Id,
                recipient.Id,
                recipient.Address,
                recipient.ProviderMessageId,
                message.SentAtUtc!.Value
            )
        )
            .AsNoTracking()
            .Take(max)
            .ToListAsync(ct);

    public async Task<Result<SentMessage>> FindByInternetMessageIdForFeedbackAsync(
        string internetMessageId,
        CancellationToken ct = default
    )
    {
        var normalized = SentMessage.NormalizeMessageId(internetMessageId);
        var id = await dbContext
            .SentMessages.IgnoreQueryFilters()
            .Where(m => m.InternetMessageId == normalized)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync(ct);

        return id is { } found
            ? await GetByIdForFeedbackAsync(found, ct)
            : Result.Failure<SentMessage>(new Error("SentMessage.NotFound", "No sent message has that Message-ID."));
    }

    public async Task<Result<SentMessage>> FindByProviderMessageIdForFeedbackAsync(
        string providerMessageId,
        string recipientAddress,
        CancellationToken ct = default
    )
    {
        var address = recipientAddress.Trim().ToLowerInvariant();
        var sentMessageId = await dbContext
            .SentMessageRecipients.IgnoreQueryFilters()
            .Where(r => r.ProviderMessageId == providerMessageId && r.Address == address)
            .Select(r => (Guid?)r.SentMessageId)
            .FirstOrDefaultAsync(ct);

        return sentMessageId is { } id
            ? await GetByIdForFeedbackAsync(id, ct)
            : Result.Failure<SentMessage>(
                new Error("SentMessage.NotFound", "No sent message matches that provider message id.")
            );
    }
}
