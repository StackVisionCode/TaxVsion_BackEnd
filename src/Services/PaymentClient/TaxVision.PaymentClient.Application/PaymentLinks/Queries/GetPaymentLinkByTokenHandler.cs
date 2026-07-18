using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.PaymentLinks.Queries;

/// <summary>
/// Público, sin JWT — el token del path es la única prueba de posesión. Cualquier razón por
/// la que el link no sea válido (no existe, vencido, ya usado, revocado) devuelve el MISMO
/// error <c>PaymentLink.NotFound</c> — nunca se distingue el motivo, para no darle a un
/// atacante una señal de "este token existió" (anti side-channel).
/// </summary>
public static class GetPaymentLinkByTokenHandler
{
    public static async Task<Result<PaymentLinkCheckoutResponse>> Handle(
        GetPaymentLinkByTokenQuery query,
        IPaymentLinkRepository links,
        ITenantPaymentConfigRepository configs,
        ITenantRegistry tenants,
        CancellationToken ct)
    {
        var notFound = new Error("PaymentLink.NotFound", "PaymentLink does not exist.");

        var link = await links.GetByTokenAsync(query.LinkToken, ct);
        if (link is null || !link.IsRedeemable(DateTime.UtcNow))
            return Result.Failure<PaymentLinkCheckoutResponse>(notFound);

        var config = await configs.GetByTenantAndProviderAsync(link.TenantId, PaymentProviderCode.Stripe, ct);
        if (config is null || !config.IsActive)
            return Result.Failure<PaymentLinkCheckoutResponse>(notFound);

        var tenant = await tenants.GetByIdAsync(link.TenantId, ct);
        if (tenant is null)
            return Result.Failure<PaymentLinkCheckoutResponse>(notFound);

        return Result.Success(new PaymentLinkCheckoutResponse(
            link.Amount.AmountCents,
            link.Amount.Currency,
            link.Purpose.Kind.ToString(),
            link.Purpose.ExternalReferenceId,
            tenant.Name,
            config.StatementDescriptor.Value,
            config.PublishableKey));
    }
}
