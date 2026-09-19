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
    bool IncludeCustomers = false
);
