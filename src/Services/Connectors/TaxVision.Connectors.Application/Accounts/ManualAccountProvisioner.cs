using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>Alta de una cuenta manual: fila nueva, reconexión en sitio o cambio desde OAuth, con sus credenciales.</summary>
public sealed class ManualAccountProvisioner(
    ITenantEmailAccountRepository accountRepository,
    IImapCredentialsRepository imapCredentialsRepository,
    ISmtpCredentialsRepository smtpCredentialsRepository,
    IEncryptedSecretProtector protector,
    AccountProviderSwitcher providerSwitcher,
    IUnitOfWork unitOfWork
)
{
    /// <summary>
    /// Resuelve contra qué fila trabajar: null = no existe (alta nueva); una cuenta = reconexión de un
    /// buzón manual desconectado (se revive reutilizando su Id y credenciales, como el flujo OAuth);
    /// Failure = duplicado real (ya conectado, o un buzón OAuth con ese email → reconectar por su vía).
    /// Scoped al tenant (uniqueness (TenantId, EmailAddress)): el mismo buzón en OTRO tenant no colisiona.
    /// </summary>
    public async Task<Result<TenantEmailAccount?>> ResolveReconnectTargetAsync(
        ConnectManualAccountCommand cmd,
        CancellationToken ct
    )
    {
        var existingResult = await accountRepository.GetByTenantAndEmailAsync(cmd.TenantId, cmd.EmailAddress, ct);
        if (existingResult.IsFailure)
            return Result.Success<TenantEmailAccount?>(null);

        var account = existingResult.Value;
        // Disconnected admite reconexión en sitio: mismo proveedor = reusa credenciales,
        // proveedor distinto (ex-OAuth → manual IMAP) = switch cross-provider con purga.
        if (account.Status == TenantEmailAccountStatus.Disconnected)
            return Result.Success<TenantEmailAccount?>(account);

        return Result.Failure<TenantEmailAccount?>(
            new Error(
                "ConnectManualAccountHandler.AlreadyConnected",
                $"'{cmd.EmailAddress}' is already connected. Disconnect it first before reconnecting."
            )
        );
    }

    public async Task<Result<TenantEmailAccount>> ProvisionAsync(
        ConnectManualAccountCommand cmd,
        TenantEmailAccount? existingAccount,
        CancellationToken ct
    )
    {
        // Fila desconectada que era personal de otra persona (p. ej. un retirado re-invitado): pasa a quien conecta.
        existingAccount?.ReassignOwnerOnReconnect(cmd.AsOffice ? null : cmd.InitiatedByUserId);

        if (existingAccount is null)
        {
            var buildResult = BuildAccountAndCredentials(cmd);
            if (buildResult.IsFailure)
                return Result.Failure<TenantEmailAccount>(buildResult.Error);
            var (account, imapCredentials, smtpCredentials) = buildResult.Value;

            await accountRepository.AddAsync(account, ct);
            await imapCredentialsRepository.AddAsync(imapCredentials, ct);
            await smtpCredentialsRepository.AddAsync(smtpCredentials, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success(account);
        }

        // Switch cross-provider: la fila existe como OAuth (Gmail/Graph) Disconnected y el usuario
        // ahora reconecta por manual IMAP. Reusamos el Id (paridad con el flujo IMAP→OAuth del
        // CompleteOAuthConnectHandler).
        var updated = ProviderSwitchPolicy.IsSwitch(existingAccount, ProviderCode.Imap)
            ? await SwitchFromOAuthToManualAsync(existingAccount, cmd, ct)
            : await ReconnectCredentialsAsync(existingAccount, cmd, ct);
        return updated.IsFailure ? Result.Failure<TenantEmailAccount>(updated.Error) : Result.Success(existingAccount);
    }

    /// <summary>
    /// Cross-provider switch: la fila existe como OAuth Disconnected y el usuario quiere pasar a manual
    /// IMAP. Simétrico del flujo IMAP→OAuth: <see cref="AccountProviderSwitcher"/> cambia el proveedor y purga lo
    /// del saliente, y acá se crean las credenciales IMAP+SMTP nuevas. Todo en la misma unidad que comitea el caller.
    /// </summary>
    private async Task<Result> SwitchFromOAuthToManualAsync(
        TenantEmailAccount account,
        ConnectManualAccountCommand cmd,
        CancellationToken ct
    )
    {
        var switched = await providerSwitcher.SwitchAsync(account, ProviderCode.Imap, DateTime.UtcNow, ct);
        if (switched.IsFailure)
            return switched;

        var imapCipher = EncryptedSecret.Create(cmd.ImapPassword, protector);
        if (imapCipher.IsFailure)
            return Result.Failure(imapCipher.Error);
        var smtpCipher = EncryptedSecret.Create(cmd.SmtpPassword, protector);
        if (smtpCipher.IsFailure)
            return Result.Failure(smtpCipher.Error);

        var imapResult = ImapCredentials.Create(
            account.Id,
            cmd.ImapHost,
            cmd.ImapPort,
            cmd.ImapUseSsl,
            cmd.ImapUsername,
            imapCipher.Value
        );
        if (imapResult.IsFailure)
            return Result.Failure(imapResult.Error);
        var smtpResult = SmtpCredentials.Create(
            account.Id,
            cmd.SmtpHost,
            cmd.SmtpPort,
            cmd.SmtpUseStartTls,
            cmd.SmtpUsername,
            smtpCipher.Value
        );
        if (smtpResult.IsFailure)
            return Result.Failure(smtpResult.Error);

        await imapCredentialsRepository.AddAsync(imapResult.Value, ct);
        await smtpCredentialsRepository.AddAsync(smtpResult.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Reconexión: el buzón Imap desconectado conserva sus filas de credenciales (1:1 por AccountId), así
    /// que se actualizan en sitio con lo que el usuario reingresó (servidor/usuario/contraseña pueden
    /// cambiar). No se recrea la cuenta: el activador la revive Disconnected→Active.
    /// </summary>
    private async Task<Result> ReconnectCredentialsAsync(
        TenantEmailAccount account,
        ConnectManualAccountCommand cmd,
        CancellationToken ct
    )
    {
        var imapCipher = EncryptedSecret.Create(cmd.ImapPassword, protector);
        if (imapCipher.IsFailure)
            return Result.Failure(imapCipher.Error);
        var smtpCipher = EncryptedSecret.Create(cmd.SmtpPassword, protector);
        if (smtpCipher.IsFailure)
            return Result.Failure(smtpCipher.Error);

        // El retiro purga las credenciales del buzón personal: si ya no están, se crean de nuevo.
        var imapResult = await imapCredentialsRepository.GetByAccountIdAsync(account.Id, ct);
        if (imapResult.IsSuccess)
            imapResult.Value.UpdateSettings(
                cmd.ImapHost,
                cmd.ImapPort,
                cmd.ImapUseSsl,
                cmd.ImapUsername,
                imapCipher.Value
            );
        else
        {
            var created = ImapCredentials.Create(
                account.Id,
                cmd.ImapHost,
                cmd.ImapPort,
                cmd.ImapUseSsl,
                cmd.ImapUsername,
                imapCipher.Value
            );
            if (created.IsFailure)
                return Result.Failure(created.Error);
            await imapCredentialsRepository.AddAsync(created.Value, ct);
        }

        var smtpResult = await smtpCredentialsRepository.GetByAccountIdAsync(account.Id, ct);
        if (smtpResult.IsSuccess)
            smtpResult.Value.UpdateSettings(
                cmd.SmtpHost,
                cmd.SmtpPort,
                cmd.SmtpUseStartTls,
                cmd.SmtpUsername,
                smtpCipher.Value
            );
        else
        {
            var created = SmtpCredentials.Create(
                account.Id,
                cmd.SmtpHost,
                cmd.SmtpPort,
                cmd.SmtpUseStartTls,
                cmd.SmtpUsername,
                smtpCipher.Value
            );
            if (created.IsFailure)
                return Result.Failure(created.Error);
            await smtpCredentialsRepository.AddAsync(created.Value, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private Result<(TenantEmailAccount Account, ImapCredentials Imap, SmtpCredentials Smtp)> BuildAccountAndCredentials(
        ConnectManualAccountCommand cmd
    )
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
}
