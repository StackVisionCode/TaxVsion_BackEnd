namespace TaxVision.Campaigns.Api.Requests;

/// <summary>
/// Agenda una campaña. <c>Recurring=false</c> → una vez en <c>RunAtUtc</c>; <c>Recurring=true</c> → primer
/// disparo en <c>RunAtUtc</c> y luego cada <c>IntervalMinutes</c>. La audiencia se resuelve en cada disparo
/// desde <c>ContactListIds</c>.
/// </summary>
public sealed record ScheduleCampaignRequest(
    bool Recurring,
    DateTime RunAtUtc,
    int? IntervalMinutes,
    IReadOnlyList<Guid>? ContactListIds,
    bool IncludeCustomers = false,
    IReadOnlyList<Guid>? CustomerIds = null,
    // Recurrencia: "Hourly" | "Daily" | "Weekly" | "Monthly" | "Custom" (default). Custom usa IntervalMinutes.
    string? Frequency = null,
    // Fin opcional (lo que ocurra primero): fecha UTC o número de disparos.
    DateTime? EndsAtUtc = null,
    int? MaxOccurrences = null
);
