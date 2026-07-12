using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Infrastructure.Persistence;

namespace TaxVision.Customer.Infrastructure.Imports;

internal sealed class SqlServerImportFileStore(CustomerDbContext db) : IImportFileStore
{
    public async Task SaveAsync(Guid importAttemptId, byte[] content, CancellationToken ct)
    {
        var entity = new CustomerImportFile
        {
            ImportAttemptId = importAttemptId,
            Content = content,
            UploadedAtUtc = DateTime.UtcNow,
        };
        await db.Set<CustomerImportFile>().AddAsync(entity, ct);
        // NO llamar SaveChanges aqui: el StartCustomerImportHandler comparte UoW y commitea al final.
    }

    public async Task<Stream> OpenReadAsync(Guid importAttemptId, CancellationToken ct)
    {
        var file =
            await db.Set<CustomerImportFile>()
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.ImportAttemptId == importAttemptId, ct)
            ?? throw new InvalidOperationException($"Import file for attempt {importAttemptId} not found in store.");

        return new MemoryStream(file.Content, writable: false);
    }

    public async Task DeleteAsync(Guid importAttemptId, CancellationToken ct)
    {
        var rows = await db.Set<CustomerImportFile>()
            .Where(f => f.ImportAttemptId == importAttemptId)
            .ExecuteDeleteAsync(ct);
        // No tira si ya esta borrado: el worker es idempotente.
        _ = rows;
    }
}
