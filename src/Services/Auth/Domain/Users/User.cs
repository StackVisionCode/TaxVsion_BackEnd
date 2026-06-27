using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;

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

    public static Result<User> Register(
        Guid tenantId,
        string name,
        string lastName,
        string email,
        string passwordHash,
        UserActorType actorType,
        Guid? customerId = null)
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
                new Error(
                    "User.PlatformScope",
                    "Platform administrators must belong to the reserved platform tenant."));
        }

        if (actorType != UserActorType.PlatformAdmin && isPlatformTenant)
        {
            return Result.Failure<User>(
                new Error(
                    "User.PlatformScope",
                    "Only platform administrators can belong to the reserved platform tenant."));
        }

        if (actorType == UserActorType.CustomerPortal &&
            (!customerId.HasValue || customerId.Value == Guid.Empty))
        {
            return Result.Failure<User>(
                new Error("User.Customer", "CustomerId is required for customer portal users."));
        }

        if (actorType != UserActorType.CustomerPortal && customerId.HasValue)
        {
            return Result.Failure<User>(
                new Error("User.Customer", "CustomerId is only valid for customer portal users."));
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
            IsActive = true
        };

        user.SetTenant(tenantId);
        user._roles.Add(UserActorRoles.For(actorType));
        return Result.Success(user);
    }

    public void Deactivate() => IsActive = false;
}
