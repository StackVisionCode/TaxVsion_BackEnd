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
public sealed class TenantEmailAccount : TenantEntity
{
    private TenantEmailAccount() { }

    public string EmailAddress { get; private set; } = default!;
    public ProviderCode ProviderCode { get; private set; }
    public string? DisplayName { get; private set; }
    public TenantEmailAccountStatus Status { get; private set; }
    public DateTime? ConnectedAtUtc { get; private set; }
    public DateTime LastActivityAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static Result<TenantEmailAccount> Create(
        Guid tenantId,
        string emailAddress,
        ProviderCode providerCode,
        Guid createdByUserId,
        DateTime createdAtUtc,
        string? displayName = null
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TenantEmailAccount>(new Error("TenantEmailAccount.Tenant", "Tenant is required."));

        var emailValidation = ValidateEmailAddress(emailAddress);
        if (emailValidation.IsFailure)
            return Result.Failure<TenantEmailAccount>(emailValidation.Error);

        if (createdByUserId == Guid.Empty)
            return Result.Failure<TenantEmailAccount>(
                new Error("TenantEmailAccount.CreatedByUserId", "CreatedByUserId is required.")
            );

        var account = new TenantEmailAccount
        {
            Id = Guid.NewGuid(),
            EmailAddress = emailValidation.Value,
            ProviderCode = providerCode,
            DisplayName = displayName is { Length: > 200 } ? displayName[..200] : displayName,
            Status = TenantEmailAccountStatus.Draft,
            LastActivityAtUtc = createdAtUtc,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = createdAtUtc,
        };
        account.SetTenant(tenantId);

        return Result.Success(account);
    }

    /// <summary>Draft|Error → Connected. El grant OAuth (o las credenciales IMAP) ya se validó.</summary>
    public Result MarkConnected(DateTime connectedAtUtc)
    {
        if (Status is not (TenantEmailAccountStatus.Draft or TenantEmailAccountStatus.Error))
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

    /// <summary>Cualquier estado salvo Disconnected → Disconnected. El usuario desconectó la cuenta.</summary>
    public Result Disconnect(DateTime disconnectedAtUtc)
    {
        if (Status == TenantEmailAccountStatus.Disconnected)
            return Result.Failure(InvalidTransition(TenantEmailAccountStatus.Disconnected));

        Status = TenantEmailAccountStatus.Disconnected;
        LastActivityAtUtc = disconnectedAtUtc;
        return Result.Success();
    }

    /// <summary>
    /// Cualquier estado activo → Error (refresh de token o renewal de watch fallaron tras reintentos,
    /// Fases 4/6). Transición unconditional — un error puede llegar desde Connected o Active por igual.
    /// </summary>
    public void MarkError(DateTime erroredAtUtc)
    {
        Status = TenantEmailAccountStatus.Error;
        LastActivityAtUtc = erroredAtUtc;
    }

    public void TouchActivity(DateTime activityAtUtc) => LastActivityAtUtc = activityAtUtc;

    private static Result<string> ValidateEmailAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return Result.Failure<string>(new Error("TenantEmailAccount.EmailAddress", "Email address is required."));

        var trimmed = address.Trim();
        if (trimmed.Length > 320 || !trimmed.Contains('@') || trimmed.StartsWith('@') || trimmed.EndsWith('@'))
            return Result.Failure<string>(
                new Error("TenantEmailAccount.EmailAddress", $"'{address}' is not a valid email address.")
            );

        return Result.Success(trimmed.ToLowerInvariant());
    }

    private Error InvalidTransition(TenantEmailAccountStatus target) =>
        new("TenantEmailAccount.InvalidTransition", $"Cannot transition TenantEmailAccount from {Status} to {target}.");
}
