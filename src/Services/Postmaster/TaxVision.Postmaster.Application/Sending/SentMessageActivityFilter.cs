using BuildingBlocks.Results;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending;

/// <summary>Filtros de "Email activity". <see cref="Status"/> null = todos.</summary>
public sealed record SentMessageActivityFilter(
    SentMessageActivity? Status,
    string? Recipient,
    DateTime? FromUtc,
    DateTime? ToUtc
)
{
    /// <summary>Un estado desconocido es un error (400), no "todos": si no, el front mostraría otra cosa sin saberlo.</summary>
    public static Result<SentMessageActivityFilter> Create(
        string? status,
        string? recipient,
        DateTime? fromUtc,
        DateTime? toUtc
    )
    {
        if (string.IsNullOrWhiteSpace(status))
            return Result.Success(new SentMessageActivityFilter(null, recipient, fromUtc, toUtc));

        return SentMessageActivityStatus.TryParse(status, out var activity)
            ? Result.Success(new SentMessageActivityFilter(activity, recipient, fromUtc, toUtc))
            : Result.Failure<SentMessageActivityFilter>(
                new Error(
                    "SentMessageActivityFilter.InvalidStatus",
                    $"Unknown status '{status}'. Allowed: {string.Join(", ", SentMessageActivityStatus.All)}."
                )
            );
    }
}
