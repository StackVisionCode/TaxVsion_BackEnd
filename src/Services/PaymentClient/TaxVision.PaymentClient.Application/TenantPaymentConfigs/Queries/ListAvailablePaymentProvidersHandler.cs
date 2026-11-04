using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Common;

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Queries;

public static class ListAvailablePaymentProvidersHandler
{
    public static Result<IReadOnlyList<AvailablePaymentProviderResponse>> Handle(
        ListAvailablePaymentProvidersQuery query,
        IPaymentAdapterFactory adapters,
        IOptions<PaymentWebhookOptions> webhookOptions
    )
    {
        IReadOnlyList<AvailablePaymentProviderResponse> response = adapters
            .GetAvailableProviders()
            .Select(descriptor => new AvailablePaymentProviderResponse(
                descriptor.Code.ToString(),
                descriptor.DisplayName,
                descriptor.PublicKeyLabel,
                descriptor.SecretKeyLabel,
                descriptor.UsesWebhookSecret,
                descriptor.WebhookSecretLabel,
                descriptor.AcceptsApiBaseUrl,
                descriptor.ApiBaseUrlHelp,
                descriptor.ApiBaseUrlPlaceholder,
                descriptor.CredentialsHelpUrl,
                descriptor.WebhookEvents ?? [],
                WebhookAutoSetup: webhookOptions.Value.IsAutoSetupEnabled
                    && PaymentWebhookAutoSetup.IsSupported(adapters, descriptor.Code)
            ))
            .ToList();

        return Result.Success(response);
    }
}
