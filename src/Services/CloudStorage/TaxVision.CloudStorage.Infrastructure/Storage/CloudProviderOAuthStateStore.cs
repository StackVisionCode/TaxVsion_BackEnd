using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Domain.Providers;
using TaxVision.CloudStorage.Infrastructure.Persistence;

namespace TaxVision.CloudStorage.Infrastructure.Storage;

/// <summary>
/// El <c>state</c> OAuth de Google Drive, OneDrive y Dropbox: se emite al conectar y se consume una
/// sola vez en el callback (anónimo), siempre para el mismo proveedor que lo emitió.
/// </summary>
public sealed class CloudProviderOAuthStateStore(CloudStorageDbContext db)
{
    public async Task<CloudProviderOAuthState> IssueAsync(
        Guid tenantId,
        Guid userId,
        string provider,
        string? returnOrigin,
        CancellationToken ct
    )
    {
        // Un intento abandonado no se borra solo: se limpian aquí los vencidos del tenant.
        var now = DateTime.UtcNow;
        var expired = await db
            .CloudProviderOAuthStates.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.ExpiresAtUtc <= now)
            .ToListAsync(ct);
        db.CloudProviderOAuthStates.RemoveRange(expired);

        var state = CloudProviderOAuthState.Create(tenantId, userId, provider, returnOrigin);
        db.CloudProviderOAuthStates.Add(state);
        await db.SaveChangesAsync(ct);
        return state;
    }

    /// <summary>
    /// Lo borra antes de cualquier llamada externa (un callback repetido o en paralelo falla cerrado) y
    /// devuelve null si no existe, venció o es de otro proveedor.
    /// </summary>
    public async Task<CloudProviderOAuthState?> ConsumeAsync(Guid stateId, string provider, CancellationToken ct)
    {
        var state = await db
            .CloudProviderOAuthStates.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Id == stateId, ct);
        if (state is null)
            return null;

        db.CloudProviderOAuthStates.Remove(state);
        await db.SaveChangesAsync(ct);
        return state.CanComplete(provider, DateTime.UtcNow) ? state : null;
    }

    /// <summary>A dónde volver: se lee antes de consumir para que hasta un error regrese al subdominio del tenant.</summary>
    public async Task<string?> FindReturnOriginAsync(string? state, CancellationToken ct)
    {
        if (!Guid.TryParseExact(state, "N", out var stateId))
            return null;
        return await db
            .CloudProviderOAuthStates.IgnoreQueryFilters()
            .Where(x => x.Id == stateId)
            .Select(x => x.ReturnOrigin)
            .SingleOrDefaultAsync(ct);
    }
}
