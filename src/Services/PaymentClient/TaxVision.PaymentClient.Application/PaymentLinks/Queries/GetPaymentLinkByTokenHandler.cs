using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;

namespace TaxVision.PaymentClient.Application.PaymentLinks.Queries;

/// <summary>
/// Público, sin JWT — el token del path es la única prueba de posesión. Si el link no es válido
/// (no existe, vencido, usado, revocado) devuelve el MISMO error <c>PaymentLink.NotFound</c> — nunca
/// se distingue el motivo (anti side-channel). Fase 2B: ya NO asume Stripe — presenta la lista de
/// métodos que el tenant tiene ACTIVOS y que además tienen adapter registrado (se pueden cobrar). Un
/// link válido sin métodos configurados devuelve 200 con la lista vacía (estado legítimo).
/// </summary>
public static class GetPaymentLinkByTokenHandler
{
    public static async Task<Result<PaymentLinkCheckoutResponse>> Handle(
        GetPaymentLinkByTokenQuery query,
        IPaymentLinkRepository links,
        ITenantPaymentConfigRepository configs,
        IPaymentAdapterFactory adapters,
        ITenantRegistry tenants,
        CancellationToken ct
    )
    {
        var notFound = new Error("PaymentLink.NotFound", "PaymentLink does not exist.");

        var link = await links.GetByTokenAsync(query.LinkToken, ct);
        if (link is null || !link.IsRedeemable(DateTime.UtcNow))
            return Result.Failure<PaymentLinkCheckoutResponse>(notFound);

        var tenant = await tenants.GetByIdAsync(link.TenantId, ct);
        if (tenant is null)
            return Result.Failure<PaymentLinkCheckoutResponse>(notFound);

        var active = await configs.GetActiveByTenantAsync(link.TenantId, ct);
        var methods = active
            .Where(c => adapters.IsRegistered(c.ProviderCode))
            .Select(c => new CheckoutPaymentMethod(
                c.ProviderCode.ToString(),
                c.ProviderCode.ToString(),
                c.StatementDescriptor.Value,
                c.PublishableKey
            ))
            .ToList();

        return Result.Success(
            new PaymentLinkCheckoutResponse(
                link.Amount.AmountCents,
                link.Amount.Currency,
                link.Purpose.Kind.ToString(),
                link.Purpose.ExternalReferenceId,
                tenant.Name,
                methods
            )
        );
    }
}
