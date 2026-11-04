using TaxVision.Tenant.Domain.Compliance;

namespace TaxVision.Tenant.Application.Compliance.Abstractions;

public interface IOfficeComplianceRepository
{
    /// <summary>null = la oficina todavía no guardó datos de cumplimiento (se responde vacío).</summary>
    Task<OfficeCompliance?> GetAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(OfficeCompliance compliance, CancellationToken ct = default);

    /// <summary>Constancia de que alguien vio el EFIN completo. Solo se agrega.</summary>
    Task AddRevealRecordAsync(EfinRevealRecord record, CancellationToken ct = default);

    /// <summary>
    /// Oficinas activas con WISP cuya fecha base (última revisión ?? adopción) es igual o anterior a
    /// <paramref name="baseOnOrBefore"/>, ordenadas por tenant y después de <paramref name="afterTenantId"/>.
    /// Filtro grueso: la decisión fina (etapa, repetición) es del agregado.
    /// </summary>
    Task<IReadOnlyList<WispReminderCandidate>> ListWispReminderCandidatesAsync(
        DateOnly baseOnOrBefore,
        Guid? afterTenantId,
        int take,
        CancellationToken ct = default
    );
}

/// <summary>Oficina a revisar por el job del WISP y su zona horaria (para saber qué día es "hoy" allí).</summary>
public sealed record WispReminderCandidate(Guid TenantId, string? TimeZoneId);
