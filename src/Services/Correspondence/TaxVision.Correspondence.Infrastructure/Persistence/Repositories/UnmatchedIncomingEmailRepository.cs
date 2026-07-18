using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

public sealed class UnmatchedIncomingEmailRepository(CorrespondenceDbContext db) : IUnmatchedIncomingEmailRepository
{
    public async Task AddAsync(UnmatchedIncomingEmail entity, CancellationToken ct = default)
    {
        await db.UnmatchedIncomingEmails.AddAsync(entity, ct);
    }
}
