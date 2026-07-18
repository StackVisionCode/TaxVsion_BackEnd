using BuildingBlocks.Results;

namespace TaxVision.Postmaster.Domain.Suppression;

public enum SuppressionReason
{
    HardBounce,
    Complaint,
    Manual,
    AbuseReport,
}

/// <summary>
/// Entrada en la lista negra de destinatarios que Postmaster nunca debe intentar enviar.
/// <c>(TenantId, EmailAddress)</c> ES la identidad (PK compuesta) — mismo criterio que
/// <c>EmailIdempotency</c>. Append-only en la práctica: se reemplaza (no se acumulan duplicados)
/// vía <see cref="Reactivate"/> si la misma dirección se vuelve a suprimir tras ser removida.
/// </summary>
public sealed class SuppressionListEntry
{
    private SuppressionListEntry() { }

    public Guid TenantId { get; private set; }
    public string EmailAddress { get; private set; } = default!;
    public SuppressionReason Reason { get; private set; }
    public DateTime AddedAtUtc { get; private set; }
    public Guid? AddedByUserId { get; private set; }
    public string? Notes { get; private set; }

    public static Result<SuppressionListEntry> Create(
        Guid tenantId,
        string emailAddress,
        SuppressionReason reason,
        Guid? addedByUserId,
        string? notes,
        DateTime addedAtUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<SuppressionListEntry>(
                new Error("SuppressionListEntry.Tenant", "Tenant is required.")
            );

        var normalized = NormalizeAddress(emailAddress);
        if (normalized is null)
            return Result.Failure<SuppressionListEntry>(
                new Error("SuppressionListEntry.EmailAddress", $"'{emailAddress}' is not a valid email address.")
            );

        if (notes is { Length: > 1000 })
            return Result.Failure<SuppressionListEntry>(
                new Error("SuppressionListEntry.Notes", "Notes must be at most 1000 chars.")
            );

        return Result.Success(
            new SuppressionListEntry
            {
                TenantId = tenantId,
                EmailAddress = normalized,
                Reason = reason,
                AddedByUserId = addedByUserId,
                Notes = notes,
                AddedAtUtc = addedAtUtc,
            }
        );
    }

    /// <summary>La dirección vuelve a suprimirse (ej. nuevo hard bounce) — refresca motivo y fecha.</summary>
    public void Reactivate(SuppressionReason reason, Guid? addedByUserId, string? notes, DateTime addedAtUtc)
    {
        Reason = reason;
        AddedByUserId = addedByUserId;
        Notes = notes;
        AddedAtUtc = addedAtUtc;
    }

    public static string? NormalizeAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;

        var trimmed = address.Trim();
        if (trimmed.Length > 320 || !trimmed.Contains('@') || trimmed.StartsWith('@') || trimmed.EndsWith('@'))
            return null;

        return trimmed.ToLowerInvariant();
    }
}
