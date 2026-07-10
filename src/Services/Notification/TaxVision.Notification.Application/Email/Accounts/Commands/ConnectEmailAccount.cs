using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Accounts;
using Wolverine;

namespace TaxVision.Notification.Application.Email.Accounts.Commands;

/// <summary>
/// Conecta una cuenta de correo externa. Los tokens/credenciales se cifran antes de persistir. Dispara
/// la primera sincronización completa de forma asíncrona.
/// </summary>
public sealed record ConnectEmailAccountCommand(
    Guid TenantId,
    Guid OwnerUserId,
    EmailExternalProvider Provider,
    string EmailAddress,
    string? DisplayName,
    string? AccessToken,
    string? RefreshToken,
    DateTime? TokenExpiresAtUtc,
    string? ExternalAccountId,
    string? ImapHost,
    int? ImapPort,
    string? ImapUsername,
    string? ImapPassword,
    bool ImapUseSsl
);

public static class ConnectEmailAccountHandler
{
    public static async Task<Result<EmailAccountResponse>> Handle(
        ConnectEmailAccountCommand command,
        IEmailAccountRepository repository,
        ISecretProtector protector,
        IMessageBus bus,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        Result<EmailAccountConnection> result;
        if (command.Provider == EmailExternalProvider.Imap)
        {
            if (string.IsNullOrWhiteSpace(command.ImapHost) || string.IsNullOrWhiteSpace(command.ImapPassword))
                return Result.Failure<EmailAccountResponse>(
                    new Error("EmailAccount.Imap", "IMAP host and password are required.")
                );

            result = EmailAccountConnection.CreateImap(
                command.TenantId,
                command.OwnerUserId,
                command.EmailAddress,
                command.DisplayName,
                command.ImapHost,
                command.ImapPort ?? 993,
                command.ImapUsername ?? command.EmailAddress,
                protector.Protect(command.ImapPassword),
                command.ImapUseSsl
            );
        }
        else
        {
            result = EmailAccountConnection.CreateOAuth(
                command.TenantId,
                command.OwnerUserId,
                command.Provider,
                command.EmailAddress,
                command.DisplayName,
                command.ExternalAccountId,
                Encrypt(protector, command.AccessToken),
                Encrypt(protector, command.RefreshToken),
                command.TokenExpiresAtUtc
            );
        }

        if (result.IsFailure)
            return Result.Failure<EmailAccountResponse>(result.Error);

        var account = result.Value;
        await repository.AddAsync(account, ct);
        await bus.PublishAsync(
            new EmailAccountConnectedIntegrationEvent
            {
                AccountId = account.Id,
                TenantId = account.TenantId,
                CorrelationId = correlation.CorrelationId,
                EmailAddress = account.EmailAddress,
            }
        );
        await bus.PublishAsync(
            new EmailFullSyncRequestedIntegrationEvent
            {
                AccountId = account.Id,
                TenantId = account.TenantId,
                CorrelationId = correlation.CorrelationId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(EmailAccountMapper.ToResponse(account));
    }

    private static string? Encrypt(ISecretProtector protector, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : protector.Protect(value);
}
