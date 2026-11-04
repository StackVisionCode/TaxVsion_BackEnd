using BuildingBlocks.Results;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending;

/// <summary>Filtros de "Email activity". <see cref="Status"/> y <see cref="Engagement"/> null = todos.</summary>
public sealed record SentMessageActivityFilter(
    SentMessageActivity? Status,
    string? Recipient,
    DateTime? FromUtc,
    DateTime? ToUtc,
    SentMessageEngagement? Engagement = null
)
{
    /// <summary>Un valor desconocido es un error (400), no "todos": si no, el front mostraría otra cosa sin saberlo.</summary>
    public static Result<SentMessageActivityFilter> Create(
        string? status,
        string? recipient,
        DateTime? fromUtc,
        DateTime? toUtc,
        string? engagement = null
    )
    {
        SentMessageActivity? activity = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!SentMessageActivityStatus.TryParse(status, out var parsed))
                return Result.Failure<SentMessageActivityFilter>(
                    new Error(
                        "SentMessageActivityFilter.InvalidStatus",
                        $"Unknown status '{status}'. Allowed: {string.Join(", ", SentMessageActivityStatus.All)}."
                    )
                );
            activity = parsed;
        }

        SentMessageEngagement? interaction = null;
        if (!string.IsNullOrWhiteSpace(engagement))
        {
            if (!SentMessageEngagementNames.TryParse(engagement, out var parsed))
                return Result.Failure<SentMessageActivityFilter>(
                    new Error(
                        "SentMessageActivityFilter.InvalidEngagement",
                        $"Unknown engagement '{engagement}'. Allowed: {string.Join(", ", SentMessageEngagementNames.All)}."
                    )
                );
            interaction = parsed;
        }

        return Result.Success(new SentMessageActivityFilter(activity, recipient, fromUtc, toUtc, interaction));
    }
}
