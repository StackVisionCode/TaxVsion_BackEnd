using System.Text.Json;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.ValueObjects;
using TaxVision.Growth.Infrastructure.Persistence;
using TaxVision.Growth.Infrastructure.Persistence.Idempotency;

namespace TaxVision.Growth.Infrastructure.Idempotency;

public sealed class SqlBusinessIdempotencyExecutor(
    GrowthDbContext dbContext,
    ITenantContext tenantContext,
    IOptions<BusinessIdempotencyOptions> options,
    TimeProvider timeProvider
) : IBusinessIdempotencyExecutor
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<TResponse>> ExecuteAsync<TResponse>(
        Guid tenantId,
        string operation,
        Guid scopeId,
        IdempotencyKey idempotencyKey,
        PayloadFingerprint payloadFingerprint,
        Func<CancellationToken, Task<Result<TResponse>>> operationBody,
        CancellationToken ct = default
    )
    {
        // tenantId explícito: el caller (handler) ya lo trae validado del comando/JWT. Antes
        // este método leía tenantContext.TenantId directamente y fallaba dentro de handlers de
        // Wolverine porque el ITenantContext ambiental llega vacío al nuevo scope de DI.
        // Defensa en profundidad: si además existe contexto ambiental (request HTTP), debe
        // coincidir con el parámetro — protege contra bugs donde el controller pase un tenantId
        // espurio dentro de una request autenticada.
        if (tenantId == Guid.Empty)
            return Failure<TResponse>(
                "Growth.Idempotency.TenantRequired",
                "A tenant is required for a mutating operation."
            );

        if (tenantContext.HasTenant && tenantContext.TenantId != tenantId)
            return Failure<TResponse>(
                "Growth.Idempotency.TenantMismatch",
                "The provided tenant does not match the active request scope."
            );

        if (string.IsNullOrWhiteSpace(operation) || operation.Length > 100 || scopeId == Guid.Empty)
            return Failure<TResponse>(
                "Growth.Idempotency.InvalidScope",
                "Operation and scope are required for idempotency."
            );

        var transaction = dbContext.Database.CurrentTransaction;
        var ownsTransaction = transaction is null;
        transaction ??= await dbContext.Database.BeginTransactionAsync(ct);

        var savepoint = ownsTransaction ? null : $"gidem_{Guid.NewGuid():N}"[..22];
        if (savepoint is not null)
        {
            if (!transaction.SupportsSavepoints)
            {
                if (ownsTransaction)
                    await transaction.DisposeAsync();

                return Failure<TResponse>(
                    "Growth.Idempotency.SavepointsRequired",
                    "The current transaction does not support the required idempotency savepoint."
                );
            }

            await transaction.CreateSavepointAsync(savepoint, ct);
        }

        var cleanupCompleted = false;
        try
        {
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            var claim = ProcessedBusinessMessage.Begin(
                tenantId,
                operation,
                scopeId,
                idempotencyKey.Value,
                payloadFingerprint.Value,
                nowUtc,
                nowUtc.AddDays(options.Value.RetentionDays)
            );
            await dbContext.ProcessedBusinessMessages.AddAsync(claim, ct);

            try
            {
                await dbContext.SaveChangesAsync(ct);
            }
            catch (ConflictException)
            {
                await RollBackAsync(transaction, ownsTransaction, savepoint, ct);
                if (!ownsTransaction && savepoint is not null)
                    await transaction.ReleaseSavepointAsync(savepoint, ct);

                cleanupCompleted = true;
                dbContext.ChangeTracker.Clear();
                if (ownsTransaction)
                    await transaction.DisposeAsync();

                return await ResolveExistingAsync<TResponse>(
                    tenantId,
                    operation,
                    scopeId,
                    idempotencyKey.Value,
                    payloadFingerprint.Value,
                    ct
                );
            }

            var result = await operationBody(ct);
            if (result.IsFailure)
            {
                await RollBackAsync(transaction, ownsTransaction, savepoint, ct);
                if (!ownsTransaction && savepoint is not null)
                    await transaction.ReleaseSavepointAsync(savepoint, ct);

                cleanupCompleted = true;
                dbContext.ChangeTracker.Clear();
                if (ownsTransaction)
                    await transaction.DisposeAsync();

                return Result.Failure<TResponse>(result.Error);
            }

            claim.Complete(
                200,
                "application/json",
                JsonSerializer.Serialize(result.Value, SerializerOptions),
                timeProvider.GetUtcNow().UtcDateTime
            );
            await dbContext.SaveChangesAsync(ct);

            if (ownsTransaction)
                await transaction.CommitAsync(ct);
            else
                await transaction.ReleaseSavepointAsync(savepoint!, ct);

            cleanupCompleted = true;
            if (ownsTransaction)
                await transaction.DisposeAsync();

            return result;
        }
        catch
        {
            if (!cleanupCompleted)
            {
                await RollBackAsync(transaction, ownsTransaction, savepoint, ct);
                dbContext.ChangeTracker.Clear();
                if (ownsTransaction)
                    await transaction.DisposeAsync();
            }

            throw;
        }
    }

    private async Task<Result<TResponse>> ResolveExistingAsync<TResponse>(
        Guid tenantId,
        string operation,
        Guid scopeId,
        string idempotencyKey,
        string fingerprint,
        CancellationToken ct
    )
    {
        var existing = await dbContext.ProcessedBusinessMessages.FirstOrDefaultAsync(
            message =>
                message.TenantId == tenantId
                && message.Operation == operation
                && message.ScopeId == scopeId
                && message.IdempotencyKey == idempotencyKey,
            ct
        );
        if (existing is null)
            return Failure<TResponse>(
                "Growth.Idempotency.ConcurrentClaimUnavailable",
                "The concurrent idempotency claim could not be resolved."
            );

        if (!existing.HasSameFingerprint(fingerprint))
            return Failure<TResponse>(
                "Growth.Idempotency.FingerprintConflict",
                "The idempotency key was already used with a different request."
            );

        if (existing.Status == ProcessedBusinessMessageStatus.Processing)
            return Failure<TResponse>(
                "Growth.Idempotency.OperationInProgress",
                "The operation with this idempotency key is still in progress."
            );

        if (
            existing.Status != ProcessedBusinessMessageStatus.Completed
            || string.IsNullOrWhiteSpace(existing.ResponseJson)
        )
            return Failure<TResponse>(
                "Growth.Idempotency.ReplayUnavailable",
                "The previous operation does not contain a replayable response."
            );

        var response = JsonSerializer.Deserialize<TResponse>(existing.ResponseJson, SerializerOptions);
        return response is null
            ? Failure<TResponse>(
                "Growth.Idempotency.InvalidStoredResponse",
                "The stored idempotent response could not be reconstructed."
            )
            : Result.Success(response);
    }

    private static async Task RollBackAsync(
        IDbContextTransaction transaction,
        bool ownsTransaction,
        string? savepoint,
        CancellationToken ct
    )
    {
        if (ownsTransaction)
            await transaction.RollbackAsync(ct);
        else if (savepoint is not null)
            await transaction.RollbackToSavepointAsync(savepoint, ct);
    }

    private static Result<T> Failure<T>(string code, string message) => Result.Failure<T>(new Error(code, message));
}
