namespace TaxVision.Customer.Domain.Employees;

/// <summary>
/// Proyeccion local de "quien es un usuario del staff del tenant y esta activo" —
/// alimentada por los eventos de ciclo de vida de usuario de Auth
/// (UserRegistered/UserDeactivated/UserReactivated). Existe para que
/// <c>Customer.AssignPreparer</c> pueda validar que <c>PreparerUserId</c> es
/// realmente un empleado activo del mismo tenant, sin que Customer dependa de una
/// llamada HTTP sincrona a Auth en el camino caliente de asignar un preparador
/// (mismo patron ya usado en este repo: CustomerEmailProjection en Signature,
/// UserDirectoryEntry en Communication).
/// </summary>
public sealed class TenantEmployeeDirectoryEntry
{
    private TenantEmployeeDirectoryEntry() { }

    /// <summary>Id de Auth — es la clave primaria (una fila por usuario, no por tenant+usuario).</summary>
    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public string ActorType { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static TenantEmployeeDirectoryEntry Create(Guid userId, Guid tenantId, string actorType, bool isActive)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId is required.", nameof(userId));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));

        return new TenantEmployeeDirectoryEntry
        {
            UserId = userId,
            TenantId = tenantId,
            ActorType = actorType,
            IsActive = isActive,
            UpdatedAtUtc = DateTime.UtcNow,
        };
    }

    public void UpdateActorType(string actorType)
    {
        ActorType = actorType;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkActive()
    {
        IsActive = true;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkInactive()
    {
        IsActive = false;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// La regla que protege AssignPreparer: solo TenantEmployee/TenantAdmin son staff
    /// interno elegible como preparador — CustomerPortal/PlatformAdmin nunca lo son.
    /// </summary>
    public bool IsEligiblePreparer => IsActive && (ActorType == "TenantEmployee" || ActorType == "TenantAdmin");
}
