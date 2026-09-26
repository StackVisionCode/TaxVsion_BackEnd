using BuildingBlocks.Common;
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
/// <see cref="CompleteOAuthConnectHandler"/>. Reusa <see cref="WatchActivationService"/> para la
/// transición Draft→Connected→Active: esa lógica ya sabe que <c>ProviderCode.Imap</c> no tiene
/// watch/subscription y activa la cuenta directo, así que no hace falta duplicarla acá — mismo camino
/// que usa Gmail/Graph tras el callback de OAuth.
///
/// Se llama en proceso (no vía <c>bus.InvokeAsync(new SetupWatchCommand(...))</c>) para compartir la
/// MISMA transacción/DbContext que <see cref="PersistAsync"/> — ver el docblock de
/// <see cref="WatchActivationService"/> para el bug real que este approach evita.
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
        IProviderWatchSubscriptionRepository watchSubscriptionRepository,
        IWatchProviderClientFactory watchClientFactory,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        // Personal: el buzón debe ser el propio email del usuario (bloquea sincronizar un correo ajeno).
        // Oficina: el manual es flexible a propósito — el dueño de la oficina puede conectar un correo
        // distinto al de su login (office@…), así que ahí el guard NO aplica.
        if (!cmd.AsOffice)
        {
            var identityCheck = ConnectedEmailIdentityGuard.Ensure(cmd.EmailAddress, cmd.InitiatorEmail);
            if (identityCheck.IsFailure)
                return Result.Failure<ConnectManualAccountResult>(identityCheck.Error);
        }

        // ¿Existe ya una fila (tenant, email)? Una DESCONECTADA de un buzón manual se reconecta
        // (revive la fila + credenciales, paridad con OAuth); cualquier otro estado = AlreadyConnected.
        var targetResult = await ResolveReconnectTargetAsync(cmd, accountRepository, ct);
        if (targetResult.IsFailure)
            return Result.Failure<ConnectManualAccountResult>(targetResult.Error);
        var existingAccount = targetResult.Value;

        var connectivityCheck = await ValidateConnectivityAsync(cmd, connectivityValidator, ct);
        if (connectivityCheck.IsFailure)
            return Result.Failure<ConnectManualAccountResult>(connectivityCheck.Error);

        TenantEmailAccount account;
        if (existingAccount is null)
        {
            var buildResult = BuildAccountAndCredentials(cmd, protector);
            if (buildResult.IsFailure)
                return Result.Failure<ConnectManualAccountResult>(buildResult.Error);
            var (newAccount, imapCredentials, smtpCredentials) = buildResult.Value;
            account = newAccount;

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
        }
        else
        {
            var reconnectResult = await ReconnectCredentialsAsync(
                existingAccount,
                cmd,
                protector,
                imapCredentialsRepository,
                smtpCredentialsRepository,
                unitOfWork,
                ct
            );
            if (reconnectResult.IsFailure)
                return Result.Failure<ConnectManualAccountResult>(reconnectResult.Error);
            account = existingAccount;
        }

        // WatchActivationService publica ConnectorsTenantEmailAccountConnected al activar; acá solo se
        // audita (antes se publicaba también acá — se quitó para no duplicar el evento).
        var activateResult = await WatchActivationService.ActivateAsync(
            cmd.TenantId,
            account.Id,
            accountRepository,
            watchSubscriptionRepository,
            watchClientFactory,
            unitOfWork,
            bus,
            correlation,
            ct
        );
        if (activateResult.IsFailure)
            return Result.Failure<ConnectManualAccountResult>(activateResult.Error);

        await RecordConnectionAuditAsync(account, auditLogRepository, unitOfWork, ct);

        return Result.Success(new ConnectManualAccountResult(account.Id, account.EmailAddress));
    }

    /// <summary>
    /// Resuelve contra qué fila trabajar: null = no existe (alta nueva); una cuenta = reconexión de un
    /// buzón manual desconectado (se revive reutilizando su Id y credenciales, como el flujo OAuth);
    /// Failure = duplicado real (ya conectado, o un buzón OAuth con ese email → reconectar por su vía).
    /// Scoped al tenant (uniqueness (TenantId, EmailAddress)): el mismo buzón en OTRO tenant no colisiona.
    /// </summary>
    private static async Task<Result<TenantEmailAccount?>> ResolveReconnectTargetAsync(
        ConnectManualAccountCommand cmd,
        ITenantEmailAccountRepository accountRepository,
        CancellationToken ct
    )
    {
        var existingResult = await accountRepository.GetByTenantAndEmailAsync(cmd.TenantId, cmd.EmailAddress, ct);
        if (existingResult.IsFailure)
            return Result.Success<TenantEmailAccount?>(null);

        var account = existingResult.Value;
        if (account.Status == TenantEmailAccountStatus.Disconnected && account.ProviderCode == ProviderCode.Imap)
            return Result.Success<TenantEmailAccount?>(account);

        return Result.Failure<TenantEmailAccount?>(
            new Error(
                "ConnectManualAccountHandler.AlreadyConnected",
                $"'{cmd.EmailAddress}' is already connected. Disconnect it first before reconnecting."
            )
        );
    }

    /// <summary>
    /// Reconexión: el buzón Imap desconectado conserva sus filas de credenciales (1:1 por AccountId), así
    /// que se actualizan en sitio con lo que el usuario reingresó (servidor/usuario/contraseña pueden
    /// cambiar). No se recrea la cuenta: <see cref="WatchActivationService"/> la revive Disconnected→Active.
    /// </summary>
    private static async Task<Result> ReconnectCredentialsAsync(
        TenantEmailAccount account,
        ConnectManualAccountCommand cmd,
        IEncryptedSecretProtector protector,
        IImapCredentialsRepository imapCredentialsRepository,
        ISmtpCredentialsRepository smtpCredentialsRepository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var imapCipher = EncryptedSecret.Create(cmd.ImapPassword, protector);
        if (imapCipher.IsFailure)
            return Result.Failure(imapCipher.Error);
        var smtpCipher = EncryptedSecret.Create(cmd.SmtpPassword, protector);
        if (smtpCipher.IsFailure)
            return Result.Failure(smtpCipher.Error);

        var imapResult = await imapCredentialsRepository.GetByAccountIdAsync(account.Id, ct);
        if (imapResult.IsFailure)
            return Result.Failure(imapResult.Error);
        imapResult.Value.UpdateSettings(cmd.ImapHost, cmd.ImapPort, cmd.ImapUseSsl, cmd.ImapUsername, imapCipher.Value);

        var smtpResult = await smtpCredentialsRepository.GetByAccountIdAsync(account.Id, ct);
        if (smtpResult.IsFailure)
            return Result.Failure(smtpResult.Error);
        smtpResult.Value.UpdateSettings(
            cmd.SmtpHost,
            cmd.SmtpPort,
            cmd.SmtpUseStartTls,
            cmd.SmtpUsername,
            smtpCipher.Value
        );

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
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
            // Oficina = sin dueño (compartido); personal = del usuario que conecta.
            ownerUserId: cmd.AsOffice ? null : cmd.InitiatedByUserId,
            displayName: cmd.DisplayName
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

    private static async Task RecordConnectionAuditAsync(
        TenantEmailAccount account,
        IProviderConnectionAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var auditResult = ProviderConnectionAuditLog.Create(
            account.Id,
            ProviderConnectionAuditAction.Connect,
            $"Connected {account.EmailAddress} via manual IMAP+SMTP.",
            "Success",
            DateTime.UtcNow
        );
        if (auditResult.IsSuccess)
        {
            await auditLogRepository.AddAsync(auditResult.Value, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
