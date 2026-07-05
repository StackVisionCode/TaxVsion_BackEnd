using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using Wolverine;

namespace TaxVision.Notification.Application.Email.Accounts.Commands;

/// <summary>Encola una sincronización (incremental o completa) de una cuenta. El proceso corre fuera del request.</summary>
public sealed record RequestAccountSyncCommand(Guid AccountId, Guid TenantId, bool Full);

public static class RequestAccountSyncHandler
{
    public static async Task<Result> Handle(
        RequestAccountSyncCommand command,
        IEmailAccountRepository repository,
        IMessageBus bus,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var account = await repository.GetByIdAsync(command.AccountId, command.TenantId, ct);
        if (account is null)
            return Result.Failure(new Error("EmailAccount.NotFound", "Account not found."));

        if (!account.IsActive)
            return Result.Failure(new Error("EmailAccount.Inactive", "The account is not active."));

        if (command.Full)
            await bus.PublishAsync(new EmailFullSyncRequestedIntegrationEvent { AccountId = account.Id, TenantId = account.TenantId, CorrelationId = correlation.CorrelationId });
        else
            await bus.PublishAsync(new EmailIncrementalSyncRequestedIntegrationEvent { AccountId = account.Id, TenantId = account.TenantId, CorrelationId = correlation.CorrelationId });

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
