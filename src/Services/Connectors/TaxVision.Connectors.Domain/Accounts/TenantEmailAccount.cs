using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Domain.Accounts;

public enum TenantEmailAccountStatus
{
    Draft,
    Connected,
    Active,
    Disconnected,
    Error,
}

/// <summary>
/// Cuenta de correo de un tenant conectada vía Gmail/Graph (OAuth) o IMAP (credenciales cifradas).
/// Connectors nunca decide si un mensaje entrante se guarda — eso es responsabilidad de Correspondence
/// filtrando contra CustomerEmailAddresses (ver Connectors_Service_Design_And_Implementation_Plan.md).
/// </summary>
public sealed class TenantEmailAccount : AggregateRoot
{
    private TenantEmailAccount() { }

    public string EmailAddress { get; private set; } = default!;
    public ProviderCode ProviderCode { get; private set; }
    public string? DisplayName { get; private set; }
    public TenantEmailAccountStatus Status { get; private set; }
    public DateTime? ConnectedAtUtc { get; private set; }
    public DateTime LastActivityAtUtc { get; private set; }

    [UserReference(
        UserReferenceKind.Credential,
        HandledBy = "OfficeMailboxesUserOffboardedConsumer",
        Note = "No se reescribe; en un buzón de oficina dispara revisión y aviso."
    )]
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Dueño lógico del buzón: null = buzón de OFICINA (compartido, lo administra el TenantAdmin);
    /// con valor = buzón PERSONAL de ese usuario (solo él lo usa/administra). Distinto de
    /// <see cref="CreatedByUserId"/>, que es solo auditoría de quién lo creó.
    /// </summary>
    [UserReference(
        UserReferenceKind.Credential,
        HandledBy = "ConnectorsUserOffboardedConsumer",
        Note = "Buzón personal: se desconecta y se purgan sus secretos."
    )]
    public Guid? OwnerUserId { get; private set; }

    public bool IsOffice => OwnerUserId is null;

    public const int ReviewReasonMaxLength = 64;

    /// <summary>Desde cuándo la oficina debe revisar este buzón de OFICINA (p. ej. lo conectó alguien que ya no está).</summary>
    public DateTime? ReviewRequiredAtUtc { get; private set; }

    /// <summary>Motivo de la revisión; ver <see cref="OfficeMailboxReviewReasons"/>.</summary>
    public string? ReviewReason { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public bool NeedsReview => ReviewRequiredAtUtc is not null;

    public static Result<TenantEmailAccount> Create(
        Guid tenantId,
        string emailAddress,
        ProviderCode providerCode,
        Guid createdByUserId,
        DateTime createdAtUtc,
        string? displayName = null,
        Guid? ownerUserId = null
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TenantEmailAccount>(new Error("TenantEmailAccount.Tenant", "Tenant is required."));

        var emailValidation = MailboxAddress.Create(emailAddress);
        if (emailValidation.IsFailure)
            return Result.Failure<TenantEmailAccount>(emailValidation.Error);

        if (createdByUserId == Guid.Empty)
            return Result.Failure<TenantEmailAccount>(
                new Error("TenantEmailAccount.CreatedByUserId", "CreatedByUserId is required.")
            );

        var account = new TenantEmailAccount
        {
            Id = Guid.NewGuid(),
            EmailAddress = emailValidation.Value.Value,
            ProviderCode = providerCode,
            DisplayName = displayName is { Length: > 200 } ? displayName[..200] : displayName,
            Status = TenantEmailAccountStatus.Draft,
            LastActivityAtUtc = createdAtUtc,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = createdAtUtc,
            OwnerUserId = ownerUserId,
        };
        account.SetTenant(tenantId);

        return Result.Success(account);
    }

    /// <summary>
    /// Draft|Error|Disconnected → Connected. El grant OAuth (o las credenciales IMAP) ya se validó.
    /// Disconnected incluido porque reconectar una cuenta que el usuario desconectó (nueva grant OAuth
    /// sobre la misma fila, ver <c>OAuthConnection.Reconnect</c>) es un flujo válido, no un error — la
    /// cuenta reusa su <see cref="Id"/> en vez de crear una nueva.
    /// </summary>
    public Result MarkConnected(DateTime connectedAtUtc)
    {
        if (
            Status
            is not (
                TenantEmailAccountStatus.Draft
                or TenantEmailAccountStatus.Error
                or TenantEmailAccountStatus.Disconnected
            )
        )
            return Result.Failure(InvalidTransition(TenantEmailAccountStatus.Connected));

        Status = TenantEmailAccountStatus.Connected;
        ConnectedAtUtc = connectedAtUtc;
        LastActivityAtUtc = connectedAtUtc;
        return Result.Success();
    }

    /// <summary>Connected → Active. El watch/subscription (Fase 6) ya quedó configurado.</summary>
    public Result Activate(DateTime activatedAtUtc)
    {
        if (Status != TenantEmailAccountStatus.Connected)
            return Result.Failure(InvalidTransition(TenantEmailAccountStatus.Active));

        Status = TenantEmailAccountStatus.Active;
        LastActivityAtUtc = activatedAtUtc;
        return Result.Success();
    }

    /// <summary>Cualquier estado salvo Disconnected → Disconnected. El usuario desconectó la cuenta (resuelve una revisión pendiente).</summary>
    public Result Disconnect(DateTime disconnectedAtUtc)
    {
        if (Status == TenantEmailAccountStatus.Disconnected)
            return Result.Failure(InvalidTransition(TenantEmailAccountStatus.Disconnected));

        Status = TenantEmailAccountStatus.Disconnected;
        LastActivityAtUtc = disconnectedAtUtc;
        ClearReview();
        return Result.Success();
    }

    /// <summary>
    /// Por qué este buzón de OFICINA queda ligado a quien se retira: es su dirección (sin distinguir
    /// mayúsculas) o fue quien lo creó/conectó. Null = no está ligado, es personal o ya está desconectado.
    /// </summary>
    public string? OffboardingReviewReasonFor(Guid userId, string? email)
    {
        if (!IsOffice || Status == TenantEmailAccountStatus.Disconnected)
            return null;
        if (MailboxAddress.Create(email) is { IsSuccess: true } address && address.Value.Matches(EmailAddress))
            return OfficeMailboxReviewReasons.AddressOfOffboardedUser;
        return userId != Guid.Empty && CreatedByUserId == userId
            ? OfficeMailboxReviewReasons.ConnectedByOffboardedUser
            : null;
    }

    /// <summary>Marca el buzón para revisión. Idempotente: si ya estaba marcado no cambia nada y devuelve false.</summary>
    public bool RequireReview(string reason, DateTime nowUtc)
    {
        if (ReviewRequiredAtUtc is not null)
            return false;
        ReviewRequiredAtUtc = nowUtc;
        ReviewReason = reason.Length > ReviewReasonMaxLength ? reason[..ReviewReasonMaxLength] : reason;
        return true;
    }

    /// <summary>
    /// Reconectar una fila DESCONECTADA que era personal de OTRA persona (un retirado re-invitado con cuenta
    /// nueva, o la oficina que recupera el buzón) la pasa al nuevo dueño: si no, quedaba a nombre del Guid
    /// viejo, invisible e inadministrable para quien la conectó. Las de oficina y las del mismo dueño no
    /// cambian; <see cref="CreatedByUserId"/> (procedencia) nunca se toca. Devuelve true si cambió.
    /// </summary>
    public bool ReassignOwnerOnReconnect(Guid? newOwnerUserId)
    {
        if (Status != TenantEmailAccountStatus.Disconnected || OwnerUserId is null || OwnerUserId == newOwnerUserId)
            return false;
        if (newOwnerUserId == Guid.Empty)
            return false;

        OwnerUserId = newOwnerUserId;
        return true;
    }

    /// <summary>Alguien vigente desconectó o volvió a autorizar el buzón: ya no hay nada que revisar.</summary>
    public void ClearReview()
    {
        ReviewRequiredAtUtc = null;
        ReviewReason = null;
    }

    /// <summary>
    /// Cualquier estado activo → Error (refresh de token o renewal de watch fallaron tras reintentos,
    /// Fases 4/6). Falla si ya estaba en Error o Disconnected: el aviso al usuario sale solo en la transición.
    /// </summary>
    public Result MarkError(TenantEmailAccountErrorCause cause, DateTime erroredAtUtc)
    {
        if (Status is TenantEmailAccountStatus.Error or TenantEmailAccountStatus.Disconnected)
            return Result.Failure(InvalidTransition(TenantEmailAccountStatus.Error));

        EnterError(cause, erroredAtUtc);
        return Result.Success();
    }

    /// <summary>
    /// Connected|Active → Error: el servidor IMAP rechazó usuario o contraseña. Falla si ya estaba en
    /// Error o Disconnected, así el aviso al usuario sale una sola vez.
    /// </summary>
    public Result MarkCredentialsRejected(TenantEmailAccountErrorCause cause, DateTime rejectedAtUtc)
    {
        if (Status is not (TenantEmailAccountStatus.Connected or TenantEmailAccountStatus.Active))
            return Result.Failure(InvalidTransition(TenantEmailAccountStatus.Error));

        EnterError(cause, rejectedAtUtc);
        return Result.Success();
    }

    private void EnterError(TenantEmailAccountErrorCause cause, DateTime erroredAtUtc)
    {
        Status = TenantEmailAccountStatus.Error;
        LastActivityAtUtc = erroredAtUtc;
        AddDomainEvent(
            new TenantEmailAccountErroredDomainEvent(
                TenantId,
                Id,
                ProviderCode,
                cause,
                CreatedByUserId,
                IsOffice,
                OwnerUserId,
                erroredAtUtc
            )
        );
    }

    public void TouchActivity(DateTime activityAtUtc) => LastActivityAtUtc = activityAtUtc;

    /// <summary>
    /// Cambia el proveedor de la cuenta en el escenario reconectar-con-otro-proveedor (p.ej. una
    /// cuenta que estaba conectada por IMAP y el usuario la desconecta y reconecta por Gmail OAuth2
    /// sobre el mismo email). Idempotente — si el proveedor es el mismo, no-op.
    ///
    /// Guardrail: solo permitido en estados "no activos" (Draft, Disconnected, Error). Para cambiar
    /// de proveedor en una cuenta Active/Connected el usuario debe desconectarla primero — así se
    /// evita que un switch silencioso deje al Watch/subscription activo contra el proveedor viejo
    /// o que una fila con credenciales de dos proveedores conviva en estado consistente.
    /// </summary>
    /// <remarks>
    /// Las credenciales del proveedor saliente (SMTP/IMAP si pasa a OAuth, o tokens OAuth si pasa a
    /// IMAP) las purga el handler que llama este método en la misma unidad de trabajo; el aggregate
    /// no es responsable de ellas (viven en agregados hermanos: SmtpCredentials/ImapCredentials/
    /// OAuthConnection). Ver <c>CompleteOAuthConnectHandler</c> / <c>ConnectManualAccountHandler</c>.
    /// </remarks>
    public Result ChangeProvider(ProviderCode newProvider, DateTime now)
    {
        if (ProviderCode == newProvider)
            return Result.Success();

        if (
            Status
            is not (
                TenantEmailAccountStatus.Draft
                or TenantEmailAccountStatus.Disconnected
                or TenantEmailAccountStatus.Error
            )
        )
            return Result.Failure(
                new Error(
                    "TenantEmailAccount.CannotChangeProvider",
                    $"Cannot change provider while account is {Status}; disconnect first."
                )
            );

        ProviderCode = newProvider;
        LastActivityAtUtc = now;
        return Result.Success();
    }

    private Error InvalidTransition(TenantEmailAccountStatus target) =>
        new("TenantEmailAccount.InvalidTransition", $"Cannot transition TenantEmailAccount from {Status} to {target}.");
}

/// <summary>Motivos de <see cref="TenantEmailAccount.ReviewReason"/>.</summary>
public static class OfficeMailboxReviewReasons
{
    /// <summary>La dirección del buzón de oficina es la de un empleado retirado.</summary>
    public const string AddressOfOffboardedUser = "AddressOfOffboardedUser";

    /// <summary>Lo creó/conectó un empleado retirado.</summary>
    public const string ConnectedByOffboardedUser = "ConnectedByOffboardedUser";
}
