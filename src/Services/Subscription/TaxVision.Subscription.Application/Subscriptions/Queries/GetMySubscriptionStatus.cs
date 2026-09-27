using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Subscriptions.Queries;

/// <summary>
/// Lo mínimo que el CRM necesita para el banner de ciclo de vida, sin un solo dato comercial: ni plan,
/// ni precio, ni límites, ni el motivo del fallo de cobro. A5 (A6.4 del plan) — es el reemplazo de
/// <c>GET subscriptions/me</c> para el shell, que hoy lo pide con cualquier token de staff.
/// </summary>
/// <param name="Status">Estado de la suscripción tal cual (<c>Active</c>, <c>PastDue</c>,
/// <c>GracePeriod</c>, <c>Suspended</c>, <c>Expired</c>, <c>Cancelled</c>, <c>Trialing</c>).</param>
/// <param name="BillingAccessBlocked">Si el acceso está cortado ahora mismo. Mismo criterio que
/// <c>TenantSubscriptionAccessConsumer</c> en Auth: Suspended o Expired.</param>
/// <param name="CanManageBilling">Si ESTE usuario puede ir a arreglarlo. El banner decide con esto si
/// ofrece el botón de renovar o solo dice "hablá con el administrador de la oficina".</param>
public sealed record MySubscriptionStatusResponse(
    string Status,
    bool BillingAccessBlocked,
    DateTime? GracePeriodEndsAtUtc,
    DateTime? NextRenewalAtUtc,
    bool CanManageBilling
);

public sealed record GetMySubscriptionStatusQuery(Guid TenantId, bool CanManageBilling);

public static class GetMySubscriptionStatusHandler
{
    public static async Task<Result<MySubscriptionStatusResponse>> Handle(
        GetMySubscriptionStatusQuery query,
        ISubscriptionRepository subscriptions,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(query.TenantId, ct);
        if (subscription is null)
            return Result.Failure<MySubscriptionStatusResponse>(
                new Error("Subscription.NotFound", "Subscription does not exist.")
            );

        return Result.Success(
            new MySubscriptionStatusResponse(
                subscription.Status.ToString(),
                subscription.Status is SubscriptionStatus.Suspended or SubscriptionStatus.Expired,
                subscription.GracePeriodEndsAtUtc,
                subscription.NextRenewalAtUtc,
                query.CanManageBilling
            )
        );
    }
}
