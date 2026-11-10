using TaxVision.Calendar.Application.Availability.Queries;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Application.Portal;

/// <summary>
/// Una cita como la ve el cliente: sin notas internas, otros invitados ni correos. Cancelar y mover los
/// decide el servidor con la política de la oficina.
/// </summary>
public sealed record PortalAppointmentResponse(
    Guid Id,
    DateTime? OriginalStartUtc,
    DateTime StartUtc,
    DateTime EndUtc,
    string TimeZoneId,
    string Title,
    string? TypeName,
    string? Location,
    bool IsVirtual,
    Guid? MeetingId,
    string PreparerDisplayName,
    string Status,
    string? MyResponse,
    bool CanCancel,
    bool CanReschedule,
    Guid TypeId,
    Guid PreparerUserId
);

public sealed record ListPortalAppointmentsQuery(Guid TenantId, Guid CustomerId, DateTime FromUtc, DateTime ToUtc);

public sealed record GetPortalAppointmentQuery(Guid TenantId, Guid CustomerId, Guid AppointmentId);

public sealed record RespondAsCustomerCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid AppointmentId,
    AttendeeResponse Response
);

public sealed record GetBookingOptionsQuery(Guid TenantId, Guid CustomerId);

/// <summary><c>Reason</c>: NotEnabled o NoPreparer cuando <c>Enabled</c> es false.</summary>
public sealed record BookingOptionsResponse(
    bool Enabled,
    string? Reason,
    IReadOnlyList<BookableTypeResponse> Types,
    IReadOnlyList<BookablePreparerResponse> Preparers,
    string? TimeZoneId,
    int CustomerChangeCutoffMinutes,
    bool RequiresApproval = false
);

public sealed record BookableTypeResponse(Guid Id, string Name, int DurationMinutes, bool IsVirtual, string ColorHex);

public sealed record BookablePreparerResponse(Guid UserId, string DisplayName);

public sealed record GetBookingSlotsQuery(
    Guid TenantId,
    Guid CustomerId,
    Guid TypeId,
    Guid? PreparerUserId,
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? RescheduleAppointmentId = null
);

public sealed record BookingSlotsResponse(IReadOnlyList<FreeSlotResponse> Slots, string TimeZoneId);

/// <summary>Nombre y correo del cliente salen de su sesión, igual que el organizador en el CRM.</summary>
public sealed record BookAsCustomerCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid PortalUserId,
    string? CustomerName,
    string? CustomerEmail,
    string IdempotencyKey,
    Guid TypeId,
    Guid? PreparerUserId,
    DateTime StartUtc
);

public sealed record CancelAsCustomerCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid PortalUserId,
    Guid AppointmentId,
    string? Reason
);

public sealed record RescheduleAsCustomerCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid PortalUserId,
    Guid AppointmentId,
    DateTime StartUtc
);
