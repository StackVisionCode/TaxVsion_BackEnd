namespace TaxVision.Subscription.Application.Catalog;

public interface ICommercialModuleShadowReconciler
{
    Task ReconcileAllAsync(CancellationToken ct);
    Task UpsertShadowForAsync(string moduleCode, CancellationToken ct);
    Task DeprecateShadowForAsync(string moduleCode, CancellationToken ct);
}
