using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Connectors.Domain.Sync;

public enum ProviderMessageFetchFailureStatus
{
    Pending,
    Abandoned,
    Resolved,
}

/// <summary>
/// Un mensaje cuya metadata no se pudo pedir de forma transitoria. Mientras está Pending frena el
/// cursor de la cuenta; si sigue fallando más allá del tope se abandona (queda para la re-sincronización).
/// </summary>
public sealed class ProviderMessageFetchFailure : BaseEntity
{
    public const int ProviderMessageIdMaxLength = 200;
    public const int LastErrorMaxLength = 500;

    private ProviderMessageFetchFailure() { }

    public Guid AccountId { get; private set; }
    public string ProviderMessageId { get; private set; } = string.Empty;
    public int Attempts { get; private set; }
    public DateTime FirstFailedAtUtc { get; private set; }
    public DateTime LastFailedAtUtc { get; private set; }
    public string? LastError { get; private set; }
    public ProviderMessageFetchFailureStatus Status { get; private set; }

    public static Result<ProviderMessageFetchFailure> Create(
        Guid accountId,
        string providerMessageId,
        string? error,
        DateTime nowUtc
    )
    {
        if (accountId == Guid.Empty)
            return Result.Failure<ProviderMessageFetchFailure>(
                new Error("ProviderMessageFetchFailure.AccountId", "AccountId is required.")
            );
        if (string.IsNullOrWhiteSpace(providerMessageId) || providerMessageId.Length > ProviderMessageIdMaxLength)
            return Result.Failure<ProviderMessageFetchFailure>(
                new Error("ProviderMessageFetchFailure.ProviderMessageId", "ProviderMessageId is invalid.")
            );

        return Result.Success(
            new ProviderMessageFetchFailure
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                ProviderMessageId = providerMessageId,
                Attempts = 1,
                FirstFailedAtUtc = nowUtc,
                LastFailedAtUtc = nowUtc,
                LastError = Truncate(error),
                Status = ProviderMessageFetchFailureStatus.Pending,
            }
        );
    }

    /// <summary>Otro fallo. Uno ya resuelto que vuelve a fallar empieza la cuenta de nuevo.</summary>
    public void RegisterFailure(string? error, DateTime nowUtc)
    {
        if (Status == ProviderMessageFetchFailureStatus.Resolved)
        {
            Status = ProviderMessageFetchFailureStatus.Pending;
            Attempts = 0;
            FirstFailedAtUtc = nowUtc;
        }

        Attempts++;
        LastFailedAtUtc = nowUtc;
        LastError = Truncate(error);
    }

    public bool ShouldAbandon(int maxAttempts, TimeSpan minAge, DateTime nowUtc) =>
        Status == ProviderMessageFetchFailureStatus.Pending
        && Attempts >= maxAttempts
        && nowUtc - FirstFailedAtUtc > minAge;

    public Result Abandon()
    {
        if (Status != ProviderMessageFetchFailureStatus.Pending)
            return Result.Failure(
                new Error("ProviderMessageFetchFailure.NotPending", "Only a pending failure can be abandoned.")
            );

        Status = ProviderMessageFetchFailureStatus.Abandoned;
        return Result.Success();
    }

    public Result Resolve()
    {
        if (Status != ProviderMessageFetchFailureStatus.Pending)
            return Result.Failure(
                new Error("ProviderMessageFetchFailure.NotPending", "Only a pending failure can be resolved.")
            );

        Status = ProviderMessageFetchFailureStatus.Resolved;
        return Result.Success();
    }

    private static string? Truncate(string? error) =>
        error is { Length: > LastErrorMaxLength } ? error[..LastErrorMaxLength] : error;
}
