using System.Security.Cryptography;
using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Domain.Invitations;

public enum InvitationStatus
{
    Pending,
    Accepted,
    Cancelled,
    Expired
}

public sealed class Invitation : TenantEntity
{
    private Invitation() { }

    public string Email { get; private set; } = default!;
    public UserActorType ActorType { get; private set; }
    public Guid? CustomerId { get; private set; }
    public Guid? InvitedByUserId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public InvitationStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? AcceptedAtUtc { get; private set; }
    public Guid? AcceptedByUserId { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public Guid? CancelledByUserId { get; private set; }

    /// <summary>Roles (JSON array de GUID) que se asignarán al usuario al aceptar.</summary>
    public string? RoleIdsJson { get; private set; }

    public int ResendCount { get; private set; }
    public DateTime? LastSentAtUtc { get; private set; }

    public static Result<Invitation> Create(
        Guid tenantId,
        string email,
        UserActorType actorType,
        Guid? customerId,
        Guid? invitedByUserId,
        string tokenHash,
        DateTime expiresAtUtc,
        string? roleIdsJson = null)
    {
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<Invitation>(
                new Error("Invitation.Tenant", "Tenant is required."));
        }

        var normalizedEmail = email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedEmail.Length == 0 || !normalizedEmail.Contains('@'))
        {
            return Result.Failure<Invitation>(
                new Error("Invitation.Email", "Invitation email is invalid."));
        }

        if (string.IsNullOrWhiteSpace(tokenHash) || tokenHash.Length != 64)
        {
            return Result.Failure<Invitation>(
                new Error("Invitation.Token", "Invitation token hash is invalid."));
        }

        if (expiresAtUtc <= DateTime.UtcNow)
        {
            return Result.Failure<Invitation>(
                new Error("Invitation.Expiration", "Invitation expiration must be in the future."));
        }

        var isPlatformTenant = tenantId == PlatformTenant.Id;
        if (actorType == UserActorType.PlatformAdmin && !isPlatformTenant)
        {
            return Result.Failure<Invitation>(
                new Error(
                    "Invitation.PlatformScope",
                    "Platform administrators can only belong to the reserved platform tenant."));
        }

        if (actorType != UserActorType.PlatformAdmin && isPlatformTenant)
        {
            return Result.Failure<Invitation>(
                new Error(
                    "Invitation.PlatformScope",
                    "The reserved platform tenant only accepts platform administrators."));
        }

        if (actorType == UserActorType.CustomerPortal &&
            (!customerId.HasValue || customerId.Value == Guid.Empty))
        {
            return Result.Failure<Invitation>(
                new Error(
                    "Invitation.Customer",
                    "CustomerId is required for customer portal invitations."));
        }

        if (actorType != UserActorType.CustomerPortal && customerId.HasValue)
        {
            return Result.Failure<Invitation>(
                new Error(
                    "Invitation.Customer",
                    "CustomerId is only valid for customer portal invitations."));
        }

        var invitation = new Invitation
        {
            Email = normalizedEmail,
            ActorType = actorType,
            CustomerId = customerId,
            InvitedByUserId = invitedByUserId,
            TokenHash = tokenHash,
            Status = InvitationStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expiresAtUtc,
            RoleIdsJson = roleIdsJson
        };
        invitation.SetTenant(tenantId);

        return Result.Success(invitation);
    }

    public const int MaxResends = 5;

    /// <summary>
    /// Regenera el token de una invitación pendiente (reenvío). El token anterior queda inválido.
    /// </summary>
    public Result Reissue(string newTokenHash, DateTime newExpiresAtUtc)
    {
        if (Status != InvitationStatus.Pending)
        {
            return Result.Failure(
                new Error("Invitation.NotPending", "Invitation is no longer pending."));
        }

        if (ResendCount >= MaxResends)
        {
            return Result.Failure(
                new Error("Invitation.ResendLimit", "Invitation resend limit reached."));
        }

        if (string.IsNullOrWhiteSpace(newTokenHash) || newTokenHash.Length != 64)
        {
            return Result.Failure(
                new Error("Invitation.Token", "Invitation token hash is invalid."));
        }

        TokenHash = newTokenHash;
        ExpiresAtUtc = newExpiresAtUtc;
        ResendCount++;
        LastSentAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public void MarkSent() => LastSentAtUtc = DateTime.UtcNow;

    public bool MatchesTokenHash(string tokenHash)
    {
        if (string.IsNullOrWhiteSpace(tokenHash) ||
            tokenHash.Length != TokenHash.Length)
        {
            return false;
        }

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(TokenHash),
                Convert.FromHexString(tokenHash));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public Result Accept(Guid userId, DateTime acceptedAtUtc)
    {
        if (Status != InvitationStatus.Pending)
        {
            return Result.Failure(
                new Error("Invitation.NotPending", "Invitation is no longer pending."));
        }

        if (acceptedAtUtc >= ExpiresAtUtc)
        {
            Status = InvitationStatus.Expired;
            return Result.Failure(
                new Error("Invitation.Expired", "Invitation has expired."));
        }

        Status = InvitationStatus.Accepted;
        AcceptedAtUtc = acceptedAtUtc;
        AcceptedByUserId = userId;
        return Result.Success();
    }

    public Result Cancel(Guid cancelledByUserId, DateTime cancelledAtUtc)
    {
        if (Status != InvitationStatus.Pending)
        {
            return Result.Failure(
                new Error("Invitation.NotPending", "Invitation is no longer pending."));
        }

        Status = InvitationStatus.Cancelled;
        CancelledAtUtc = cancelledAtUtc;
        CancelledByUserId = cancelledByUserId;
        return Result.Success();
    }

    public bool MarkExpired(DateTime utcNow)
    {
        if (Status != InvitationStatus.Pending || utcNow < ExpiresAtUtc)
            return false;

        Status = InvitationStatus.Expired;
        return true;
    }
}
