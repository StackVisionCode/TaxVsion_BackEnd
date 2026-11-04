using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Audit;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Audit;
using TaxVision.Connectors.Domain.Shared;
using Wolverine;

namespace TaxVision.Connectors.Application.Accounts.Consumers;

/// <summary>
/// Al RETIRAR (offboard) a un empleado: sus buzones PERSONALES (OwnerUserId == el que se va) se
/// desconectan y se PURGAN sus secretos (OAuth token, IMAP/SMTP). NO se reasignan: las credenciales
/// son de la cuenta de correo personal del empleado, no transferibles a otra persona. Los buzones de
/// OFICINA (OwnerUserId null) no se tocan aquí (ver <see cref="OfficeMailboxesUserOffboardedConsumer"/>).
/// La correspondencia histórica vive en Correspondence y se conserva. Idempotente (al reprocesar, las
/// filas de credenciales ya no están y se saltan). La revocación al proveedor es best-effort.
/// </summary>
public static class ConnectorsUserOffboardedConsumer
{
    public static async Task Handle(
        UserOffboardedIntegrationEvent msg,
        ITenantEmailAccountRepository accounts,
        IOAuthConnectionRepository oauthConnections,
        IImapCredentialsRepository imapCredentials,
        ISmtpCredentialsRepository smtpCredentials,
        IProviderWatchSubscriptionRepository watchSubscriptions,
        IOAuthProviderClientFactory clientFactory,
        IEncryptedSecretProtector protector,
        IProviderConnectionAuditLogRepository auditLog,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TenantEmailAccount> logger,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(
            string.IsNullOrWhiteSpace(msg.CorrelationId) ? msg.EventId.ToString("N") : msg.CorrelationId
        );

        var personalMailboxes = await accounts.ListByOwnerUserAsync(msg.TenantId, msg.UserId, ct);
        if (personalMailboxes.Count == 0)
            return;

        var now = DateTime.UtcNow;
        foreach (var account in personalMailboxes)
        {
            // 1. Desconectar (frena polling/envío). Si ya estaba Disconnected el Result falla y se ignora;
            //    igual seguimos con la purga.
            account.Disconnect(now);

            // 2. Revocar OAuth (best-effort al proveedor) y purgar connection + token.
            var connectionResult = await oauthConnections.GetByAccountIdAsync(account.Id, ct);
            if (connectionResult.IsSuccess)
            {
                var connection = connectionResult.Value;
                connection.Revoke(now);
                await ProviderGrantRevocation.TryRevokeAsync(account, connection, clientFactory, protector, logger, ct);
                oauthConnections.Remove(connection);
            }

            // 2b. Sin token no hay users.stop: el push de entrada se da de baja y deja de renovarse.
            var watchResult = await watchSubscriptions.GetByAccountIdAsync(account.Id, ct);
            if (watchResult.IsSuccess)
                watchResult.Value.MarkRemoved();

            // 3. Purgar credenciales IMAP/SMTP guardadas (contraseñas personales del que se va).
            var imapResult = await imapCredentials.GetByAccountIdAsync(account.Id, ct);
            if (imapResult.IsSuccess)
                imapCredentials.Remove(imapResult.Value);

            var smtpResult = await smtpCredentials.GetByAccountIdAsync(account.Id, ct);
            if (smtpResult.IsSuccess)
                smtpCredentials.Remove(smtpResult.Value);

            var audit = ProviderConnectionAuditLog.Create(
                account.Id,
                ProviderConnectionAuditAction.Disconnect,
                "Personal mailbox disconnected and secrets purged: owner offboarded.",
                "Success",
                now
            );
            if (audit.IsSuccess)
                await auditLog.AddAsync(audit.Value, ct);
        }

        // Guardar antes de publicar: si el guardado falla y se reintenta, el aviso no sale repetido.
        await unitOfWork.SaveChangesAsync(ct);

        foreach (var account in personalMailboxes)
            await bus.PublishAsync(
                new ConnectorsTenantEmailAccountDisconnectedIntegrationEvent
                {
                    CorrelationId = correlation.CorrelationId,
                    TenantId = msg.TenantId,
                    AccountId = account.Id,
                    EmailAddress = account.EmailAddress,
                    ProviderCode = account.ProviderCode.ToString(),
                    DisconnectedAtUtc = now,
                }
            );

        logger.LogInformation(
            "Offboarded user {UserId}: disconnected and purged {Count} personal mailbox(es) in tenant {TenantId}.",
            msg.UserId,
            personalMailboxes.Count,
            msg.TenantId
        );
    }
}
