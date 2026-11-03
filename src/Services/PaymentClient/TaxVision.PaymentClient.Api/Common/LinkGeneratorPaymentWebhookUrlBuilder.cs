using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions.Payments;

namespace TaxVision.PaymentClient.Api.Common;

/// <summary>URL pública del webhook: la ruta sale del enrutado por su nombre (igual que el catálogo del
/// CRM) y la base de <see cref="PaymentWebhookOptions.PublicBaseUrl"/>. Sin HttpContext: corre en el
/// comando asíncrono de registro.</summary>
public sealed class LinkGeneratorPaymentWebhookUrlBuilder(LinkGenerator links, IOptions<PaymentWebhookOptions> options)
    : IPaymentWebhookUrlBuilder
{
    public Uri? Build(string webhookRouteName, Guid tenantId)
    {
        var baseUrl = options.Value.PublicBaseUrl;
        if (
            string.IsNullOrWhiteSpace(baseUrl)
            || links.GetPathByName(webhookRouteName, new { tenantId }) is not { } path
        )
            return null;

        return Uri.TryCreate($"{baseUrl.Trim().TrimEnd('/')}{path}", UriKind.Absolute, out var url) ? url : null;
    }
}
