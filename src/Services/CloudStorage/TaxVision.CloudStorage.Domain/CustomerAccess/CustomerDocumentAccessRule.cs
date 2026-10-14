using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.CloudStorage.Domain.CustomerAccess;

/// <summary>
/// Regla deny per-item sobre un cliente. La existencia de la fila ES el deny; su ausencia ES el allow
/// (los roles dan el allow, deny gana a allow). Sin herencia folder→file: cada capability sobre cada
/// item se declara explícita. Un cliente comparte reglas con su joint-filing (principal = cliente, no
/// usuario del portal).
/// </summary>
public sealed class CustomerDocumentAccessRule : TenantEntity
{
    private CustomerDocumentAccessRule() { }

    public Guid CustomerId { get; private set; }
    public AccessResourceType ResourceType { get; private set; }
    public Guid ResourceId { get; private set; }
    public AccessCapability Capability { get; private set; }
    public Guid SetByUserId { get; private set; }
    public DateTime SetAtUtc { get; private set; }
    public string? Reason { get; private set; }

    public static Result<CustomerDocumentAccessRule> Create(
        Guid id,
        Guid tenantId,
        Guid customerId,
        AccessResourceType resourceType,
        Guid resourceId,
        AccessCapability capability,
        Guid setByUserId,
        DateTime setAtUtc,
        string? reason
    )
    {
        if (tenantId == Guid.Empty || customerId == Guid.Empty || resourceId == Guid.Empty || setByUserId == Guid.Empty)
            return Result.Failure<CustomerDocumentAccessRule>(CustomerAccessErrors.InvalidIdentifiers);

        var sanitizedReason = SanitizeReason(reason);
        if (sanitizedReason.IsFailure)
            return Result.Failure<CustomerDocumentAccessRule>(sanitizedReason.Error);

        var rule = new CustomerDocumentAccessRule
        {
            Id = id,
            CustomerId = customerId,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Capability = capability,
            SetByUserId = setByUserId,
            SetAtUtc = setAtUtc,
            Reason = sanitizedReason.Value,
        };
        rule.SetTenant(tenantId);
        return Result.Success(rule);
    }

    /// <summary>Idempotencia del set: el mismo (customer, item, capability) se renueva — mantiene la fila, refresca autor/fecha/reason.</summary>
    public Result Refresh(Guid setByUserId, DateTime setAtUtc, string? reason)
    {
        if (setByUserId == Guid.Empty)
            return Result.Failure(CustomerAccessErrors.InvalidIdentifiers);

        var sanitizedReason = SanitizeReason(reason);
        if (sanitizedReason.IsFailure)
            return Result.Failure(sanitizedReason.Error);

        SetByUserId = setByUserId;
        SetAtUtc = setAtUtc;
        Reason = sanitizedReason.Value;
        return Result.Success();
    }

    private static Result<string?> SanitizeReason(string? reason)
    {
        if (reason is null)
            return Result.Success<string?>(null);
        var trimmed = reason.Trim();
        if (trimmed.Length == 0)
            return Result.Success<string?>(null);
        if (trimmed.Length > 500)
            return Result.Failure<string?>(CustomerAccessErrors.InvalidReason);
        return Result.Success<string?>(trimmed);
    }
}
