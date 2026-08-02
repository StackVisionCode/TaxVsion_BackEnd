namespace BuildingBlocks.RateLimiting;

/// <summary>
/// Wrapper inyectable sobre <see cref="RateLimitPolicyCatalog"/> — el catálogo en sí es estático
/// (mismo criterio que <c>PermissionCatalog</c>), pero el middleware de Fase 3 necesita un puerto
/// para poder fake-earlo en tests sin tocar el catálogo real.
/// </summary>
public interface IRateLimitPolicyRegistry
{
    RateLimitPolicyDefinition GetByName(string policyName);
    IReadOnlyCollection<RateLimitPolicyDefinition> All { get; }
}

public sealed class RateLimitPolicyRegistry : IRateLimitPolicyRegistry
{
    public RateLimitPolicyDefinition GetByName(string policyName) => RateLimitPolicyCatalog.GetByName(policyName);

    public IReadOnlyCollection<RateLimitPolicyDefinition> All => RateLimitPolicyCatalog.All;
}
