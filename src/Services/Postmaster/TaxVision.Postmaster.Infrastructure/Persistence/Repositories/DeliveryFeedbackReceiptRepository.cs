using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Application.Feedback;
using TaxVision.Postmaster.Domain.Feedback;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Repositories;

public sealed class DeliveryFeedbackReceiptRepository(PostmasterDbContext dbContext)
    : IDeliveryFeedbackReceiptRepository
{
    public Task<bool> ExistsAsync(string source, string sourceEventId, CancellationToken ct = default) =>
        dbContext.DeliveryFeedbackReceipts.AnyAsync(r => r.Source == source && r.SourceEventId == sourceEventId, ct);

    public async Task<bool> TryCommitAsync(DeliveryFeedbackReceipt receipt, CancellationToken ct = default)
    {
        await dbContext.DeliveryFeedbackReceipts.AddAsync(receipt, ct);
        try
        {
            await dbContext.SaveChangesAsync(ct);
            return true;
        }
        catch (ConflictException)
        {
            // Otra copia del mismo aviso ganó. Se descarta lo que este intento había cambiado.
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }
}
