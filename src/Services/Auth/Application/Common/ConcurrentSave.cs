using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Common;

/// <summary>La unidad de trabajo puede soltar lo pendiente tras un choque, para que el commit del handler no lo reintente.</summary>
public interface IPendingChangesDiscarder
{
    void DiscardPendingChanges();
}

/// <summary>
/// Guardado que pierde una carrera de <c>RowVersion</c> (03 §18.4): devuelve false y descarta lo pendiente, así el
/// handler responde su error de dominio en vez del conflicto genérico. La transacción vuelve al punto previo.
/// </summary>
public static class ConcurrentSave
{
    public static async Task<bool> TrySaveAsync(IUnitOfWork unitOfWork, CancellationToken ct)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
            return true;
        }
        catch (ConflictException conflict) when (conflict.Code == WolverineConflictPolicy.ConcurrencyConflictCode)
        {
            (unitOfWork as IPendingChangesDiscarder)?.DiscardPendingChanges();
            return false;
        }
    }
}
