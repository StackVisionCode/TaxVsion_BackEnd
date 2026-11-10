using System.Text.Json;
using BuildingBlocks.Domain;

namespace TaxVision.Calendar.Domain.Office;

/// <summary>
/// Copia local del horario, los cierres y la zona de la oficina (Tenant). El perfil llega en
/// <c>TenantProfileChanged</c> (ordenado por versión) y la zona en <c>TenantUpdated</c> (ordenada por
/// fecha). Se crea con el primer evento que llegue: no depende de un relleno previo.
/// </summary>
public sealed class OfficeScheduleProjection : TenantEntity
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private OfficeScheduleProjection() { }

    public string? TimeZoneId { get; private set; }
    public DateTime? TimeZoneUpdatedAtUtc { get; private set; }
    public long ProfileVersion { get; private set; }
    public string HoursJson { get; private set; } = "[]";
    public string ClosuresJson { get; private set; } = "[]";
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyList<OfficeHours> Hours => Read<OfficeHours>(HoursJson);
    public IReadOnlyList<OfficeClosureSpan> Closures => Read<OfficeClosureSpan>(ClosuresJson);

    public static OfficeScheduleProjection Create(Guid tenantId, DateTime nowUtc)
    {
        var projection = new OfficeScheduleProjection { Id = Guid.NewGuid(), UpdatedAtUtc = nowUtc };
        projection.SetTenant(tenantId);
        return projection;
    }

    /// <summary>Aplica el perfil si es más nuevo; una versión igual o menor se ignora.</summary>
    public bool ApplyProfile(
        long version,
        IReadOnlyList<OfficeHours> hours,
        IReadOnlyList<OfficeClosureSpan> closures,
        DateTime nowUtc
    )
    {
        if (version <= ProfileVersion)
            return false;

        ProfileVersion = version;
        HoursJson = JsonSerializer.Serialize(hours, Json);
        ClosuresJson = JsonSerializer.Serialize(closures, Json);
        UpdatedAtUtc = nowUtc;
        return true;
    }

    /// <summary>Aplica la zona si el cambio es más nuevo que el que ya tiene.</summary>
    public bool ApplyTimeZone(string timeZoneId, DateTime changedAtUtc, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) || TimeZoneUpdatedAtUtc >= changedAtUtc)
            return false;

        TimeZoneId = timeZoneId.Trim();
        TimeZoneUpdatedAtUtc = changedAtUtc;
        UpdatedAtUtc = nowUtc;
        return true;
    }

    /// <summary>
    /// El horario para calcular disponibilidad. Sin zona conocida se usa la del preparador: la oficina y
    /// su gente suelen compartirla, y es mejor que ignorar el horario.
    /// </summary>
    public OfficeSchedule? ToSchedule(string? fallbackZoneId)
    {
        var zone = TimeZoneId ?? fallbackZoneId;
        return string.IsNullOrWhiteSpace(zone) ? null : OfficeSchedule.Of(zone, Hours, Closures);
    }

    private static List<T> Read<T>(string json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<T>>(json, Json) ?? [];
}
