using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Audit;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Audit;
using TaxVision.Connectors.Domain.Shared;
using Wolverine.Attributes;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>
/// Buzones PERSONALES (OwnerUserId == el que se va) del empleado retirado: se desconectan y se PURGAN sus secretos
/// (OAuth token, IMAP/SMTP). NO se reasignan: las credenciales son de la cuenta personal, no transferibles. La
/// correspondencia histórica vive en Correspondence y se conserva. Idempotente (al reprocesar, las filas de
/// credenciales ya no están y se saltan).
/// <para>Sin transacción del handler: la purga y los avisos van en una unidad corta, y la revocación al proveedor
/// (best-effort) después del commit.</para>
/// </summary>
public static class PurgeOffboardedMailboxesHandler
{
    [NonTransactional]
    public static async Task Handle(
        PurgeOffboardedMailboxesCommand command,
        ITenantEmailAccountRepository accounts,
        IOAuthConnectionRepository oauthConnections,
        IImapCredentialsRepository imapCredentials,
        ISmtpCredentialsRepository smtpCredentials,
        IProviderWatchSubscriptionRepository watchSubscriptions,
        IOAuthProviderClientFactory clientFactory,
        IEncryptedSecretProtector protector,
        IProviderConnectionAuditLogRepository auditLog,
        IOutboxUnit unit,
        ICorrelationContext correlation,
        ILogger<TenantEmailAccount> logger,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(command.CorrelationId);

        var personalMailboxes = await accounts.ListByOwnerUserAsync(command.TenantId, command.UserId, ct);
        if (personalMailboxes.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var revocations = new List<(TenantEmailAccount Account, OAuthConnection Connection)>();
        await unit.RunAsync(
            async (outbox, token) =>
            {
                foreach (var account in personalMailboxes)
                {
                    // 1. Desconectar (frena polling/envío). Si ya estaba Disconnected el Result falla y se ignora;
                    //    igual seguimos con la purga.
                    account.Disconnect(now);

                    // 2. Purgar connection + token. Se revoca en el proveedor al final, ya confirmado.
                    var connectionResult = await oauthConnections.GetByAccountIdAsync(account.Id, token);
                    if (connectionResult.IsSuccess)
                    {
                        var connection = connectionResult.Value;
                        connection.Revoke(now);
                        revocations.Add((account, connection));
                        oauthConnections.Remove(connection);
                    }

                    // 2b. Sin token no hay users.stop: el push de entrada se da de baja y deja de renovarse.
                    var watchResult = await watchSubscriptions.GetByAccountIdAsync(account.Id, token);
                    if (watchResult.IsSuccess)
                        watchResult.Value.MarkRemoved();

                    // 3. Purgar credenciales IMAP/SMTP guardadas (contraseñas personales del que se va).
                    var imapResult = await imapCredentials.GetByAccountIdAsync(account.Id, token);
                    if (imapResult.IsSuccess)
                        imapCredentials.Remove(imapResult.Value);

                    var smtpResult = await smtpCredentials.GetByAccountIdAsync(account.Id, token);
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
                        await auditLog.AddAsync(audit.Value, token);

                    await outbox.PublishAsync(
                        new ConnectorsTenantEmailAccountDisconnectedIntegrationEvent
                        {
                            CorrelationId = correlation.CorrelationId,
                            TenantId = command.TenantId,
                            AccountId = account.Id,
                            EmailAddress = account.EmailAddress,
                            ProviderCode = account.ProviderCode.ToString(),
                            DisconnectedAtUtc = now,
                            OwnerUserId = account.OwnerUserId,
                            IsOffice = account.IsOffice,
                        }
                    );
                }
                return true;
            },
            ct
        );

        // Ya confirmado: el refresh token se descifra de la copia en memoria.
        foreach (var (account, connection) in revocations)
            await ProviderGrantRevocation.TryRevokeAsync(account, connection, clientFactory, protector, logger, ct);

        logger.LogInformation(
            "Offboarded user {UserId}: disconnected and purged {Count} personal mailbox(es) in tenant {TenantId}.",
            command.UserId,
            personalMailboxes.Count,
            command.TenantId
        );
    }
}
