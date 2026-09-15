using BuildingBlocks.RateLimiting;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.RateLimit;

/// <summary>
/// Contrato NO-breaking de <see cref="ITenantPlanCodeProjection"/>: un servicio que aún NO migró la
/// columna de módulos implementa solo el overload de 2 args de <c>ApplyIfNewer</c>. Verifica que los
/// defaults de la interfaz (Open/Closed) lo mantienen funcionando sin cambios:
/// <list type="bullet">
///   <item><see cref="ITenantPlanCodeProjection.EnabledModules"/> devuelve <c>[]</c>.</item>
///   <item>El overload de 3 args (default interface method) delega en el de 2 args, ignorando módulos.</item>
/// </list>
/// </summary>
public sealed class TenantPlanCodeProjectionDefaultMethodTests
{
    /// <summary>Proyección legada: solo persiste plan+revisión, no conoce módulos.</summary>
    private sealed class LegacyProjection : ITenantPlanCodeProjection
    {
        public Guid TenantId { get; }
        public string PlanCode { get; private set; } = "free";
        public long RevisionNumber { get; private set; }

        // Implementa SOLO el overload de 2 args — el de 3 args viene del default de la interfaz.
        public void ApplyIfNewer(string planCode, long revisionNumber)
        {
            if (revisionNumber < RevisionNumber)
                return;
            PlanCode = planCode;
            RevisionNumber = revisionNumber;
        }
    }

    [Fact]
    public void EnabledModules_defaults_to_empty_for_legacy_projection()
    {
        ITenantPlanCodeProjection projection = new LegacyProjection();
        Assert.Empty(projection.EnabledModules);
    }

    [Fact]
    public void Three_arg_overload_delegates_to_two_arg_ignoring_modules()
    {
        ITenantPlanCodeProjection projection = new LegacyProjection();

        // Llamar el overload de 3 args con módulos: el default los descarta y aplica plan+revisión.
        projection.ApplyIfNewer("pro", 7, ["signatures", "documents"]);

        Assert.Equal("pro", projection.PlanCode);
        Assert.Equal(7, projection.RevisionNumber);
        Assert.Empty(projection.EnabledModules); // el default no persiste módulos
    }

    [Fact]
    public void Three_arg_overload_honors_the_monotonic_revision_guard()
    {
        ITenantPlanCodeProjection projection = new LegacyProjection();
        projection.ApplyIfNewer("pro", 10, ["signatures"]);

        // Revisión más vieja vía el overload de 3 args → el guard del 2-arg la rechaza.
        projection.ApplyIfNewer("free", 5, []);

        Assert.Equal("pro", projection.PlanCode);
        Assert.Equal(10, projection.RevisionNumber);
    }
}
