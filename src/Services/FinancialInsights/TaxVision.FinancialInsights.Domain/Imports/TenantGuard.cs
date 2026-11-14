using BuildingBlocks.Results;

namespace TaxVision.FinancialInsights.Domain.Imports;

// El tenant se fija al crear; nunca se mueve a otro.
internal static class TenantGuard
{
    public static Result CanAssign(Guid current, Guid requested)
    {
        if (requested == Guid.Empty)
            return Result.Failure(FinancialInsightsErrors.TenantRequired);
        if (current != Guid.Empty && current != requested)
            return Result.Failure(FinancialInsightsErrors.TenantImmutable);
        return Result.Success();
    }
}
