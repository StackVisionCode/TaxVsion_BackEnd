using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Application.Imports.Consumers;

/// <summary>
/// Al retirar a un empleado con un import en curso, el aviso de fin (completo, cancelado o fallido) pasa a
/// quien ejecutó el retiro o, si no se puede, al sucesor; solo si está activo en el directorio local
/// (fail-closed). El import sigue corriendo y <c>CreatedByUserId</c> no cambia (procedencia). Idempotente:
/// lo traspasado sale del filtro.
/// </summary>
public static class CustomerImportsUserOffboardedConsumer
{
    public static async Task Handle(
        UserOffboardedIntegrationEvent msg,
        ICustomerImportRepository imports,
        ITenantEmployeeDirectoryRepository directory,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<CustomerImportAttempt> logger,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(
            string.IsNullOrWhiteSpace(msg.CorrelationId) ? msg.EventId.ToString("N") : msg.CorrelationId
        );

        var inFlight = await imports.ListInFlightNotifyingAsync(msg.TenantId, msg.UserId, ct);
        if (inFlight.Count == 0)
            return;

        if (await ResolveRecipientAsync(msg, directory, ct) is not { } recipient)
        {
            logger.LogWarning(
                "Offboarded user {UserId} has {Count} import(s) in flight and nobody active to notify in tenant {TenantId}.",
                msg.UserId,
                inFlight.Count,
                msg.TenantId
            );
            return;
        }

        var handedOver = inFlight.Count(attempt => attempt.HandOverFollowUp(recipient).IsSuccess);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation(
            "Offboarded user {UserId}: {Count} import completion notice(s) handed over to {RecipientId} in tenant {TenantId}.",
            msg.UserId,
            handedOver,
            recipient,
            msg.TenantId
        );
    }

    // Quien ejecutó el retiro primero (es quien sabe del import); si no, el sucesor.
    private static async Task<Guid?> ResolveRecipientAsync(
        UserOffboardedIntegrationEvent msg,
        ITenantEmployeeDirectoryRepository directory,
        CancellationToken ct
    )
    {
        foreach (var candidate in new[] { msg.OffboardedByUserId, msg.SuccessorUserId })
        {
            if (candidate is not { } userId || userId == msg.UserId || userId == Guid.Empty)
                continue;
            var entry = await directory.GetByUserIdAsync(userId, ct);
            if (entry is { IsActive: true, IsOffboarded: false } && entry.TenantId == msg.TenantId)
                return userId;
        }

        return null;
    }
}
