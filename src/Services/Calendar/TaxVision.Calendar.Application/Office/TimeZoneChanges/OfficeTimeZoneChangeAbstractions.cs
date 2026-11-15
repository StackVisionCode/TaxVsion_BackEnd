using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Availability;
using TaxVision.Calendar.Domain.Office;

namespace TaxVision.Calendar.Application.Office.TimeZoneChanges;

public interface IOfficeTimeZoneChangeRepository
{
    void Add(OfficeTimeZoneChange change);
    Task<OfficeTimeZoneChange?> GetPendingAsync(Guid tenantId, CancellationToken ct = default);
    Task<bool> ExistsForVersionAsync(Guid tenantId, long tenantVersion, CancellationToken ct = default);
}

/// <summary>Lo que un cambio de zona puede mover: horarios activos y series no canceladas de esa zona.</summary>
public interface IOfficeTimeZoneCandidates
{
    Task<IReadOnlyList<AvailabilityRule>> ActiveRulesInZoneAsync(
        Guid tenantId,
        string timeZoneId,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<Appointment>> LiveSeriesInZoneAsync(
        Guid tenantId,
        string timeZoneId,
        CancellationToken ct = default
    );
}
