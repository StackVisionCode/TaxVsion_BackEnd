using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Connectors.Domain.Audit;

public enum ProviderConnectionAuditAction
{
    Connect,
    Refresh,
    WatchRenew,
    Disconnect,
    Error,
    BodyFetch,
    AttachmentFetch,
    MessageSend,
}

/// <summary>
/// Registro append-only de actividad sobre una cuenta conectada (§15 del plan) — nunca se
/// modifica tras crearse, solo se agrega. Primer uso real: Fase 8 (BodyFetch), pero el enum de
/// acciones ya cubre las de fases previas para que un futuro trabajo de instrumentación no tenga
/// que ampliarlo de nuevo.
/// </summary>
public sealed class ProviderConnectionAuditLog : BaseEntity
{
    private ProviderConnectionAuditLog() { }

    public Guid AccountId { get; private set; }
    public ProviderConnectionAuditAction Action { get; private set; }
    public string Detail { get; private set; } = default!;
    public string ResultCode { get; private set; } = default!;
    public DateTime Timestamp { get; private set; }

    public static Result<ProviderConnectionAuditLog> Create(
        Guid accountId,
        ProviderConnectionAuditAction action,
        string detail,
        string resultCode,
        DateTime timestamp
    )
    {
        if (accountId == Guid.Empty)
            return Result.Failure<ProviderConnectionAuditLog>(
                new Error("ProviderConnectionAuditLog.AccountId", "AccountId is required.")
            );

        if (string.IsNullOrWhiteSpace(resultCode) || resultCode.Length > 100)
            return Result.Failure<ProviderConnectionAuditLog>(
                new Error(
                    "ProviderConnectionAuditLog.ResultCode",
                    "ResultCode is required and must be at most 100 chars."
                )
            );

        return Result.Success(
            new ProviderConnectionAuditLog
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                Action = action,
                Detail = detail is { Length: > 2000 } ? detail[..2000] : detail ?? string.Empty,
                ResultCode = resultCode,
                Timestamp = timestamp,
            }
        );
    }
}
