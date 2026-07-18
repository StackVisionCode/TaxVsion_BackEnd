using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Audit;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Audit;
using TaxVision.Connectors.Domain.Shared;
using Wolverine;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>
/// Orquesta el flujo de conectar una cuenta manual (D3 Compose §8/§11.1) — la contraparte sin OAuth de
/// <see cref="CompleteOAuthConnectHandler"/>. Reusa <c>SetupWatchCommand</c> para la transición
/// Draft→Connected→Active: ese handler ya sabe que <c>ProviderCode.Imap</c> no tiene watch/subscription
/// y activa la cuenta directo (ver <c>SetupWatchHandler</c>), así que no hace falta duplicar esa lógica
/// acá — mismo camino que usa Gmail/Graph tras el callback de OAuth.
/// </summary>
public static class ConnectManualAccountHandler
{
    public static async Task<Result<ConnectManualAccountResult>> Handle(
        ConnectManualAccountCommand cmd,
        ITenantEmailAccountRepository accountRepository,
        IImapCredentialsRepository imapCredentialsRepository,
        ISmtpCredentialsRepository smtpCredentialsRepository,
        IManualAccountConnectivityValidator connectivityValidator,
        IEncryptedSecretProtector protector,
        IProviderConnectionAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var duplicateCheck = await EnsureEmailNotAlreadyConnectedAsync(cmd, accountRepository, ct);
        if (duplicateCheck.IsFailure)
            return Result.Failure<ConnectManualAccountResult>(duplicateCheck.Error);

        var connectivityCheck = await ValidateConnectivityAsync(cmd, connectivityValidator, ct);
        if (connectivityCheck.IsFailure)
            return Result.Failure<ConnectManualAccountResult>(connectivityCheck.Error);

        var buildResult = BuildAccountAndCredentials(cmd, protector);
        if (buildResult.IsFailure)
            return Result.Failure<ConnectManualAccountResult>(buildResult.Error);
        var (account, imapCredentials, smtpCredentials) = buildResult.Value;

        await PersistAsync(
            account,
            imapCredentials,
            smtpCredentials,
            accountRepository,
            imapCredentialsRepository,
            smtpCredentialsRepository,
            unitOfWork,
            ct
        );

        var activateResult = await bus.InvokeAsync<Result>(new SetupWatchCommand(cmd.TenantId, account.Id), ct);
        if (activateResult.IsFailure)
            return Result.Failure<ConnectManualAccountResult>(activateResult.Error);

        await PublishConnectedEventAndAuditAsync(cmd, account, bus, auditLogRepository, unitOfWork, ct);

        return Result.Success(new ConnectManualAccountResult(account.Id, account.EmailAddress));
    }

    private static async Task<Result> EnsureEmailNotAlreadyConnectedAsync(
        ConnectManualAccountCommand cmd,
        ITenantEmailAccountRepository accountRepository,
        CancellationToken ct
    )
    {
        var existingResult = await accountRepository.GetByEmailAddressAsync(cmd.EmailAddress, ct);
        if (existingResult.IsFailure)
            return Result.Success();

        return existingResult.Value.TenantId != cmd.TenantId
            ? Result.Failure(
                new Error(
                    "ConnectManualAccountHandler.EmailBelongsToAnotherTenant",
                    $"'{cmd.EmailAddress}' is already connected under a different tenant."
                )
            )
            : Result.Failure(
                new Error(
                    "ConnectManualAccountHandler.AlreadyConnected",
                    $"'{cmd.EmailAddress}' is already connected. Disconnect it first before reconnecting."
                )
            );
    }

    /// <summary>
    /// Prueba real de conectividad antes de persistir nada — evita que credenciales mal tipeadas
    /// queden guardadas como si estuvieran bien (§ IManualAccountConnectivityValidator).
    /// </summary>
    private static async Task<Result> ValidateConnectivityAsync(
        ConnectManualAccountCommand cmd,
        IManualAccountConnectivityValidator connectivityValidator,
        CancellationToken ct
    )
    {
        var imapCheck = await connectivityValidator.ValidateImapAsync(
            cmd.ImapHost,
            cmd.ImapPort,
            cmd.ImapUseSsl,
            cmd.ImapUsername,
            cmd.ImapPassword,
            ct
        );
        if (imapCheck.IsFailure)
            return imapCheck;

        return await connectivityValidator.ValidateSmtpAsync(
            cmd.SmtpHost,
            cmd.SmtpPort,
            cmd.SmtpUseStartTls,
            cmd.SmtpUsername,
            cmd.SmtpPassword,
            ct
        );
    }

    private static Result<(
        TenantEmailAccount Account,
        ImapCredentials Imap,
        SmtpCredentials Smtp
    )> BuildAccountAndCredentials(ConnectManualAccountCommand cmd, IEncryptedSecretProtector protector)
    {
        var accountResult = TenantEmailAccount.Create(
            cmd.TenantId,
            cmd.EmailAddress,
            ProviderCode.Imap,
            cmd.InitiatedByUserId,
            DateTime.UtcNow,
            cmd.DisplayName
        );
        if (accountResult.IsFailure)
            return Result.Failure<(TenantEmailAccount, ImapCredentials, SmtpCredentials)>(accountResult.Error);
        var account = accountResult.Value;

        var imapCipherResult = EncryptedSecret.Create(cmd.ImapPassword, protector);
        if (imapCipherResult.IsFailure)
            return Result.Failure<(TenantEmailAccount, ImapCredentials, SmtpCredentials)>(imapCipherResult.Error);

        var imapResult = ImapCredentials.Create(
            account.Id,
            cmd.ImapHost,
            cmd.ImapPort,
            cmd.ImapUseSsl,
            cmd.ImapUsername,
            imapCipherResult.Value
        );
        if (imapResult.IsFailure)
            return Result.Failure<(TenantEmailAccount, ImapCredentials, SmtpCredentials)>(imapResult.Error);

        var smtpCipherResult = EncryptedSecret.Create(cmd.SmtpPassword, protector);
        if (smtpCipherResult.IsFailure)
            return Result.Failure<(TenantEmailAccount, ImapCredentials, SmtpCredentials)>(smtpCipherResult.Error);

        var smtpResult = SmtpCredentials.Create(
            account.Id,
            cmd.SmtpHost,
            cmd.SmtpPort,
            cmd.SmtpUseStartTls,
            cmd.SmtpUsername,
            smtpCipherResult.Value
        );
        if (smtpResult.IsFailure)
            return Result.Failure<(TenantEmailAccount, ImapCredentials, SmtpCredentials)>(smtpResult.Error);

        return Result.Success((account, imapResult.Value, smtpResult.Value));
    }

    private static async Task PersistAsync(
        TenantEmailAccount account,
        ImapCredentials imapCredentials,
        SmtpCredentials smtpCredentials,
        ITenantEmailAccountRepository accountRepository,
        IImapCredentialsRepository imapCredentialsRepository,
        ISmtpCredentialsRepository smtpCredentialsRepository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        await accountRepository.AddAsync(account, ct);
        await imapCredentialsRepository.AddAsync(imapCredentials, ct);
        await smtpCredentialsRepository.AddAsync(smtpCredentials, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private static async Task PublishConnectedEventAndAuditAsync(
        ConnectManualAccountCommand cmd,
        TenantEmailAccount account,
        IMessageBus bus,
        IProviderConnectionAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        await bus.PublishAsync(
            new ConnectorsTenantEmailAccountConnectedIntegrationEvent
            {
                TenantId = cmd.TenantId,
                AccountId = account.Id,
                EmailAddress = account.EmailAddress,
                ProviderCode = ProviderCode.Imap.ToString(),
                ConnectedAtUtc = now,
            }
        );

        var auditResult = ProviderConnectionAuditLog.Create(
            account.Id,
            ProviderConnectionAuditAction.Connect,
            $"Connected {account.EmailAddress} via manual IMAP+SMTP.",
            "Success",
            now
        );
        if (auditResult.IsSuccess)
        {
            await auditLogRepository.AddAsync(auditResult.Value, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
