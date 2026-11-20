using System.Text.Json;
using BuildingBlocks.Domain;
using BuildingBlocks.RateLimiting;

namespace TaxVision.Mobile.Domain.RateLimiting;

/// <summary>
/// Proyección local del plan del tenant y de sus módulos habilitados (los <c>module.*</c> del snapshot de
/// Subscription). La lee el gate de módulo para <c>mileage</c>. Mismo patrón que Notes; el handler compartido
/// de BuildingBlocks opera sobre ella por <see cref="ITenantPlanCodeProjection"/>.
/// </summary>
public sealed class TenantPlanCodeProjection : TenantEntity, ITenantPlanCodeProjection
{
    private TenantPlanCodeProjection() { }

    public string PlanCode { get; private set; } = string.Empty;
    public long RevisionNumber { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public string EnabledModulesJson { get; private set; } = "[]";

    /// <inheritdoc />
    public IReadOnlyList<string> EnabledModules => Deserialize(EnabledModulesJson);

    public static TenantPlanCodeProjection Create(Guid tenantId, string planCode, long revisionNumber)
    {
        var projection = new TenantPlanCodeProjection
        {
            Id = Guid.NewGuid(),
            PlanCode = planCode,
            RevisionNumber = revisionNumber,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        projection.SetTenant(tenantId);
        return projection;
    }

    public void ApplyIfNewer(string planCode, long revisionNumber) =>
        ApplyIfNewer(planCode, revisionNumber, EnabledModules);

    /// <summary>Aplica plan y módulos solo si la revisión no es más vieja que la actual.</summary>
    public void ApplyIfNewer(string planCode, long revisionNumber, IReadOnlyList<string> enabledModules)
    {
        if (revisionNumber < RevisionNumber)
            return;
        PlanCode = planCode;
        RevisionNumber = revisionNumber;
        EnabledModulesJson = Serialize(enabledModules);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string Serialize(IReadOnlyList<string> modules) => JsonSerializer.Serialize(modules);

    private static IReadOnlyList<string> Deserialize(string json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<string>>(json) ?? [];
}
