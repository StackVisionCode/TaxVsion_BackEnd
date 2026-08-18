using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Api.Requests;

// ---------------------------------------------------------------------------
// Ningún request lleva TenantId ni UserId: salen del JWT vía ControllerIdentityExtensions. Aceptarlos
// del body sería exactamente el agujero que se cerró en Auth con Login/ForgotPassword.
// ---------------------------------------------------------------------------

/// <summary>
/// <c>RequestKey</c> es obligatoria y la pone el cliente (ADR-R-07). Es lo que hace que un reintento
/// por timeout de red no cree dos recordatorios.
/// </summary>
public sealed record CreateReminderRequest(
    string? Title,
    string? Body,
    ReminderCategory Category,
    Guid? TargetId,
    DateTime? FireAtUtc,
    DateTime? AnchorAtUtc,
    int? LeadMinutes,
    string? TimeZone,
    string? RequestKey
);

public sealed record UpdateReminderScheduleRequest(DateTime? FireAtUtc, DateTime? AnchorAtUtc, int? LeadMinutes);

public sealed record UpdateReminderSubjectRequest(string? Title, string? Body);

public sealed record SnoozeReminderRequest(int Minutes);

public sealed record CancelReminderRequest(string? Reason);
