using BuildingBlocks.Results;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Application.Feedback;
using TaxVision.Postmaster.Domain.Feedback;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Repositories;

public sealed class DeliveryFeedbackReceiptRepository(PostmasterDbContext dbContext)
    : IDeliveryFeedbackReceiptRepository
{
    private const string ReceiptIndex = "UX_DeliveryFeedbackReceipts_Source_SourceEventId";

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
        catch (ConflictException ex) when (IsDuplicateReceipt(ex))
        {
            // Otra copia del mismo aviso ganó. Se descarta lo que este intento había cambiado.
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    // Cualquier otro choque (la supresión, la versión del correo) no es este aviso repetido: sale y se reintenta.
    private static bool IsDuplicateReceipt(ConflictException ex) =>
        ex.InnerException?.InnerException is SqlException sql
        && sql.Message.Contains(ReceiptIndex, StringComparison.Ordinal);
}
