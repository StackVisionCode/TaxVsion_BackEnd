using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Infrastructure.Persistence.Queries;

/// <summary>Reglas que comparten el listado y los números del dashboard, para que el total de la lista sea el número.</summary>
internal static class SignatureRequestListFilters
{
    /// <summary>Ventana de "vence pronto" (expiringWithin48h del resumen).</summary>
    public static readonly TimeSpan ExpiringSoonWindow = TimeSpan.FromHours(48);

    // En curso con vencimiento en (now, now + 48 h].
    public static IQueryable<SignatureRequest> ExpiringSoon(this IQueryable<SignatureRequest> requests, DateTime now)
    {
        var soon = now.Add(ExpiringSoonWindow);
        return requests.Where(r =>
            r.Status == SignatureRequestStatus.InProgress && r.ExpiresAtUtc > now && r.ExpiresAtUtc <= soon
        );
    }

    // Rol "waiting" de SignatureWork: autora y sin su propia firma de preparadora pendiente.
    public static IQueryable<SignatureRequest> AwaitingOthersFor(
        this IQueryable<SignatureRequest> requests,
        Guid userId
    ) =>
        requests.Where(r =>
            r.CreatedByUserId == userId
            && !(r.Preparer != null && r.Preparer.UserId == userId && r.PreparerSignedByUserId == null)
        );
}
