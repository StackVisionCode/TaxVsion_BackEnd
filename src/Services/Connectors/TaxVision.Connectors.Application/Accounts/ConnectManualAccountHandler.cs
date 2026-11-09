using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Watch;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>
/// Orquesta el flujo de conectar una cuenta manual (D3 Compose §8/§11.1) — la contraparte sin OAuth de
/// <see cref="CompleteOAuthConnectHandler"/>. Reusa <see cref="WatchActivationService"/> para la
/// transición Draft→Connected→Active: esa lógica ya sabe que <c>ProviderCode.Imap</c> no tiene
/// watch/subscription y activa la cuenta directo, así que no hace falta duplicarla acá — mismo camino
/// que usa Gmail/Graph tras el callback de OAuth.
///
/// Se llama en proceso (no vía <c>bus.InvokeAsync(new SetupWatchCommand(...))</c>) para compartir la
/// MISMA transacción/DbContext que <see cref="ManualAccountProvisioner"/> — ver el docblock de
/// <see cref="WatchActivationService"/> para el bug real que este approach evita.
///
/// CON-12: la prueba IMAP/SMTP tarda segundos y va antes, sin transacción. Lo que se guarda va en una sola
/// unidad: si algo falla no queda nada.
/// </summary>
public static class ConnectManualAccountHandler
{
    [NonTransactional]
    public static async Task<Result<ConnectManualAccountResult>> Handle(
        ConnectManualAccountCommand cmd,
        ManualAccountProvisioner provisioner,
        IManualAccountConnectivityValidator connectivityValidator,
        AccountConnectionFinisher finisher,
        IUserPermissionsProjectionReader initiatorProjections,
        IUnitOfWork unitOfWork,
        IOutboxUnit unit,
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
        var targetResult = await provisioner.ResolveReconnectTargetAsync(cmd, ct);
        if (targetResult.IsFailure)
            return Result.Failure<ConnectManualAccountResult>(targetResult.Error);
        var existingAccount = targetResult.Value;

        var connectivityCheck = await ManualAccountConnectivityCheck.ValidateAsync(cmd, connectivityValidator, ct);
        if (connectivityCheck.IsFailure)
            return Result.Failure<ConnectManualAccountResult>(connectivityCheck.Error);

        // La prueba IMAP/SMTP tarda segundos: si lo retiraron o suspendieron mientras tanto, no se guarda nada.
        var initiatorCheck = await ConnectInitiatorGuard.EnsureActiveAsync(
            cmd.TenantId,
            cmd.InitiatedByUserId,
            cmd.InitiatorIsPlatformAdmin,
            initiatorProjections,
            ct
        );
        if (initiatorCheck.IsFailure)
            return Result.Failure<ConnectManualAccountResult>(initiatorCheck.Error);

        var outcome = Result.Failure<ConnectManualAccountResult>(
            new Error("ConnectManualAccountHandler.NotSaved", "The account could not be saved.")
        );
        await unit.RunAsync(
            async (outbox, token) =>
            {
                outcome = await SaveAndActivateAsync(outbox, token);
                return outcome.IsSuccess;
            },
            ct
        );
        return outcome;

        async Task<Result<ConnectManualAccountResult>> SaveAndActivateAsync(IMessageBus outbox, CancellationToken token)
        {
            var provisioned = await provisioner.ProvisionAsync(cmd, existingAccount, token);
            if (provisioned.IsFailure)
                return Result.Failure<ConnectManualAccountResult>(provisioned.Error);
            var account = provisioned.Value;

            var activateResult = await finisher.ActivateAsync(
                cmd.TenantId,
                account.Id,
                new HandlerTransactionUnit(unitOfWork, outbox),
                token
            );
            if (activateResult.IsFailure)
                return Result.Failure<ConnectManualAccountResult>(activateResult.Error);

            await finisher.AuditConnectedAsync(
                account,
                $"Connected {account.EmailAddress} via manual IMAP+SMTP.",
                DateTime.UtcNow,
                token
            );

            return Result.Success(new ConnectManualAccountResult(account.Id, account.EmailAddress));
        }
    }
}
