using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Consents;

namespace TaxVision.Tasks.ClientForms.Application.Consents;

internal static class FormConsentEvidenceWriter
{
    private const string UniqueViolation = "Persistence.UniqueConstraint";

    // Idempotente: si otra petición ya guardó la misma evidencia, se devuelve esa y no se crea otra.
    internal static async Task<Result<(FormConsentRecord Record, bool Created)>> RecordAsync(
        FormConsentRecordKey key,
        Func<Result<FormConsentRecord>> create,
        IFormConsentRecordRepository records,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var existing = await records.GetAsync(key, ct);
        if (existing is not null)
            return Result.Success((existing, false));
        var created = create();
        if (created.IsFailure)
            return Result.Failure<(FormConsentRecord, bool)>(created.Error);
        records.Add(created.Value);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success((created.Value, true));
        }
        catch (ConflictException conflict) when (conflict.Code == UniqueViolation)
        {
            records.Discard(created.Value);
            var winner = await records.GetAsync(key, ct);
            if (winner is null)
                throw;
            return Result.Success((winner, false));
        }
    }
}
