using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Projections;

namespace TaxVision.Correspondence.Application.Projections;

/// <summary>Mantiene <see cref="MailboxOwnership"/>. Conectar otra vez también trae al dueño nuevo, si cambió.</summary>
public static class MailboxOwnershipProjectionConsumer
{
    public static async Task Handle(
        ConnectorsTenantEmailAccountConnectedIntegrationEvent evt,
        IMailboxOwnershipRepository ownership,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var change = new Change(
                evt.TenantId,
                evt.AccountId,
                evt.OwnerUserId,
                evt.IsOffice,
                IsActive: true,
                evt.ConnectedAtUtc
            );
            await ApplyAsync(change, ownership, unitOfWork, ct);
        }
    }

    public static async Task Handle(
        ConnectorsTenantEmailAccountDisconnectedIntegrationEvent evt,
        IMailboxOwnershipRepository ownership,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var change = new Change(
                evt.TenantId,
                evt.AccountId,
                evt.OwnerUserId,
                evt.IsOffice,
                IsActive: false,
                evt.DisconnectedAtUtc
            );
            await ApplyAsync(change, ownership, unitOfWork, ct);
        }
    }

    private static async Task ApplyAsync(
        Change change,
        IMailboxOwnershipRepository ownership,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        // Sin dueño ni oficina, el evento es anterior a estos campos: lo completa la reconciliación.
        var knowsTheOwner = change.OwnerUserId is not null || change.IsOffice;
        var row = await ownership.GetAsync(change.TenantId, change.AccountId, ct);
        if (row is not null)
            row.Apply(knowsTheOwner ? change.OwnerUserId : row.OwnerUserId, change.IsActive, change.AtUtc);
        else if (knowsTheOwner)
            await ownership.AddAsync(
                MailboxOwnership.Create(
                    change.TenantId,
                    change.AccountId,
                    change.OwnerUserId,
                    change.IsActive,
                    change.AtUtc
                ),
                ct
            );

        await unitOfWork.SaveChangesAsync(ct);
    }

    private sealed record Change(
        Guid TenantId,
        Guid AccountId,
        Guid? OwnerUserId,
        bool IsOffice,
        bool IsActive,
        DateTime AtUtc
    );
}
