using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Repositories;

public sealed class SentMessageRepository(PostmasterDbContext dbContext) : ISentMessageRepository
{
    public async Task AddAsync(SentMessage message, CancellationToken ct = default) =>
        await dbContext.SentMessages.AddAsync(message, ct);

    public async Task<Result<SentMessage>> GetByIdWithEventsAsync(
        Guid tenantId,
        Guid id,
        CancellationToken ct = default
    )
    {
        var message = await dbContext
            .SentMessages.Include(m => m.Events)
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenantId, ct);

        return message is null
            ? Result.Failure<SentMessage>(new Error("SentMessage.NotFound", $"SentMessage {id} not found."))
            : Result.Success(message);
    }
}
