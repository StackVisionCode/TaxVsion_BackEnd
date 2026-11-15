using BuildingBlocks.Tenancy;

namespace TaxVision.Calendar.Application.PublicBookings;

/// <summary>Reglas de la reserva pública que no son de una página (sección "Calendar:PublicBooking").</summary>
public sealed class PublicBookingOptions
{
    public const string SectionName = "Calendar:PublicBooking";

    /// <summary>Tenants que pueden publicar páginas. Vacío = solo la plataforma: una oficina no publica.</summary>
    public Guid[] AllowedTenantIds { get; set; } = [];

    /// <summary>Minutos que el horario queda retenido mientras el visitante confirma su email.</summary>
    public int HoldMinutes { get; set; } = 10;

    /// <summary>Solicitudes por email y hora; las de más se aceptan en silencio sin efecto.</summary>
    public int MaxRequestsPerEmailPerHour { get; set; } = 3;

    /// <summary>Horas que se guarda una solicitud que nunca se confirmó (D-04).</summary>
    public int UnverifiedRetentionHours { get; set; } = 72;

    /// <summary>Meses tras el cierre de una demo hasta anonimizarla (D-04).</summary>
    public int ClosedRetentionMonths { get; set; } = 24;

    /// <summary>Rango máximo de una consulta de horarios.</summary>
    public int MaxSlotRangeDays { get; set; } = 14;

    public IReadOnlyCollection<Guid> EffectiveTenantIds =>
        AllowedTenantIds.Length > 0 ? AllowedTenantIds : [PlatformTenant.Id];
}
