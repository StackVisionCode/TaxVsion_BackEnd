using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using Wolverine;

namespace TaxVision.Notification.Application.Email.Accounts.Commands;

/// <summary>Desconecta una cuenta (borra los secretos, detiene la sincronización).</summary>
public sealed record DisconnectEmailAccountCommand(Guid AccountId, Guid TenantId);

public static class DisconnectEmailAccountHandler
{
    public static async Task<Result> Handle(
        DisconnectEmailAccountCommand command,
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

        account.Disconnect();
        await bus.PublishAsync(
            new EmailAccountDisconnectedIntegrationEvent
            {
                AccountId = account.Id,
                TenantId = account.TenantId,
                CorrelationId = correlation.CorrelationId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
