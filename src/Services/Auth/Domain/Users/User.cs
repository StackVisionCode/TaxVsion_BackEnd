using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.TimeZones;

namespace TaxVision.Auth.Domain.Users;

public sealed class User : TenantEntity
{
    private readonly List<string> _roles = [];

    private User() { }

    public string Name { get; private set; } = default!;
    public string LastName { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public UserActorType ActorType { get; private set; }
    public Guid? CustomerId { get; private set; }
    public bool IsActive { get; private set; }
    public IReadOnlyCollection<string> Roles => _roles.AsReadOnly();

    // Perfil y credenciales
    public string? TimeZoneId { get; private set; }
    public string? PhoneNumber { get; private set; }
    public bool EmailVerified { get; private set; }
    public bool PhoneVerified { get; private set; }
    public DateTime? PasswordChangedAtUtc { get; private set; }

    // MFA
    public bool MfaEnabled { get; private set; }

    // Protección contra fuerza bruta
    public int FailedLoginCount { get; private set; }
    public DateTime? LockoutEndUtc { get; private set; }

    // Invalidación de permisos en JWT emitidos
    public int PermissionsVersion { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? DeactivatedAtUtc { get; private set; }

    public static Result<User> Register(
        Guid tenantId,
        string name,
        string lastName,
        string email,
        string passwordHash,
        UserActorType actorType,
        Guid? customerId = null
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<User>(new Error("User.Tenant", "Tenant is required."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<User>(new Error("User.Name", "Name is required."));

        if (string.IsNullOrWhiteSpace(lastName))
            return Result.Failure<User>(new Error("User.LastName", "Last name is required."));

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return Result.Failure<User>(new Error("User.Email", "Email is invalid."));

        if (string.IsNullOrWhiteSpace(passwordHash))
            return Result.Failure<User>(new Error("User.Password", "Password hash is required."));

        var isPlatformTenant = tenantId == PlatformTenant.Id;
        if (actorType == UserActorType.PlatformAdmin && !isPlatformTenant)
        {
            return Result.Failure<User>(
                new Error("User.PlatformScope", "Platform administrators must belong to the reserved platform tenant.")
            );
        }

        if (actorType != UserActorType.PlatformAdmin && isPlatformTenant)
        {
            return Result.Failure<User>(
                new Error(
                    "User.PlatformScope",
                    "Only platform administrators can belong to the reserved platform tenant."
                )
            );
        }

        if (actorType == UserActorType.CustomerPortal && (!customerId.HasValue || customerId.Value == Guid.Empty))
        {
            return Result.Failure<User>(
                new Error("User.Customer", "CustomerId is required for customer portal users.")
            );
        }

        if (actorType != UserActorType.CustomerPortal && customerId.HasValue)
        {
            return Result.Failure<User>(
                new Error("User.Customer", "CustomerId is only valid for customer portal users.")
            );
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            LastName = lastName.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            ActorType = actorType,
            CustomerId = customerId,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        };

        user.SetTenant(tenantId);
        user._roles.Add(UserActorRoles.For(actorType));
        return Result.Success(user);
    }

    public bool IsLockedOut(DateTime utcNow) => LockoutEndUtc is { } end && end > utcNow;

    public void RegisterFailedLogin(DateTime utcNow, int maxAttempts, TimeSpan lockoutDuration)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= maxAttempts)
        {
            LockoutEndUtc = utcNow.Add(lockoutDuration);
            FailedLoginCount = 0;
        }
    }

    public void RegisterSuccessfulLogin()
    {
        FailedLoginCount = 0;
        LockoutEndUtc = null;
    }

    public Result ChangePassword(string newPasswordHash, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
            return Result.Failure(new Error("User.Password", "Password hash is required."));

        PasswordHash = newPasswordHash;
        PasswordChangedAtUtc = utcNow;
        return Result.Success();
    }

    public Result UpdateProfile(string name, string lastName)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(new Error("User.Name", "Name is required."));

        if (string.IsNullOrWhiteSpace(lastName))
            return Result.Failure(new Error("User.LastName", "Last name is required."));

        Name = name.Trim();
        LastName = lastName.Trim();
        return Result.Success();
    }

    public Result SetTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            TimeZoneId = null;
            return Result.Success();
        }

        if (!IanaTimeZone.TryNormalize(timeZoneId, out var normalized))
        {
            return Result.Failure(new Error("User.TimeZone", "Time zone must be a valid IANA identifier."));
        }

        TimeZoneId = normalized;
        return Result.Success();
    }

    public void ChangeEmail(string newEmail)
    {
        Email = newEmail.Trim().ToLowerInvariant();
        EmailVerified = true;
    }

    public void SetPhoneNumber(string phoneNumber)
    {
        PhoneNumber = phoneNumber.Trim();
        PhoneVerified = true;
    }

    public void VerifyEmail() => EmailVerified = true;

    public void EnableMfa() => MfaEnabled = true;

    public void DisableMfa() => MfaEnabled = false;

    public void BumpPermissionsVersion() => PermissionsVersion++;

    public void Deactivate(DateTime utcNow)
    {
        IsActive = false;
        DeactivatedAtUtc = utcNow;
    }

    public void Reactivate()
    {
        IsActive = true;
        DeactivatedAtUtc = null;
    }
}
