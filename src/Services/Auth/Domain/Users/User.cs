using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Auth.Domain.Users.Events;

namespace TaxVision.Auth.Domain.Users;

public sealed class User : BaseEntity, ITenantOwned
{
    private readonly List<string> _roles = [];

    private User() { }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string LasName { get; private set; }
    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public IReadOnlyCollection<string> Roles => _roles.AsReadOnly();

    public static Result<User> Register(Guid tenantId, string name, string lastname, string email, string passwordHash)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<User>(new Error("User.Tenant", "Tenant is required."));

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return Result.Failure<User>(new Error("User.Email", "Email is invalid."));

        if (string.IsNullOrWhiteSpace(passwordHash))
            return Result.Failure<User>(new Error("User.Password", "Password hash is required."));

        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            IsActive = true
        };

        user._roles.Add("User");
        user.Raise(new UserRegisteredDomainEvent(user.Id, tenantId));
        return Result.Success(user);
    }

    public void AssignRole(string role)
    {
        if (!string.IsNullOrWhiteSpace(role) && !_roles.Contains(role))
            _roles.Add(role);
    }

    public void SetTenantId(Guid tenantId) => TenantId = tenantId;
}
