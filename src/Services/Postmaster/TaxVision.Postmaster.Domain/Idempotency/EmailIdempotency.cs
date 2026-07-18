using BuildingBlocks.Results;

namespace TaxVision.Postmaster.Domain.Idempotency;

/// <summary>
/// Registro técnico de idempotencia por <c>(TenantId, IdempotencyKey)</c> — esa tupla ES la
/// identidad (PK compuesta), no lleva un Id propio. Reserva primero (<see cref="CompletedAtUtc"/>
/// null) y se completa cuando el envío efectivamente termina, para que un evento duplicado dentro
/// de la ventana de reserva no dispare un segundo envío.
/// </summary>
public sealed class EmailIdempotency
{
    private EmailIdempotency() { }

    public Guid TenantId { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    public Guid? SentMessageId { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static Result<EmailIdempotency> Reserve(
        Guid tenantId,
        string idempotencyKey,
        DateTime nowUtc,
        TimeSpan timeToLive
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<EmailIdempotency>(new Error("EmailIdempotency.Tenant", "Tenant is required."));

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
            return Result.Failure<EmailIdempotency>(
                new Error(
                    "EmailIdempotency.IdempotencyKey",
                    "IdempotencyKey is required and must be at most 200 chars."
                )
            );

        return Result.Success(
            new EmailIdempotency
            {
                TenantId = tenantId,
                IdempotencyKey = idempotencyKey,
                CreatedAtUtc = nowUtc,
                ExpiresAtUtc = nowUtc.Add(timeToLive),
            }
        );
    }

    /// <summary>Marca la reserva como terminada con el envío ya materializado.</summary>
    public Result Complete(Guid sentMessageId, DateTime completedAtUtc)
    {
        if (CompletedAtUtc is not null)
            return Result.Failure(
                new Error(
                    "EmailIdempotency.AlreadyCompleted",
                    $"Reservation for '{IdempotencyKey}' was already completed."
                )
            );

        SentMessageId = sentMessageId;
        CompletedAtUtc = completedAtUtc;
        return Result.Success();
    }
}
