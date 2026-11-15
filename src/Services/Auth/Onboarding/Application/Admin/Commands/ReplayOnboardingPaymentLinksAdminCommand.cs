using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Auth.Onboarding.Application.Abstractions;
using Wolverine;

namespace TaxVision.Auth.Onboarding.Application.Admin.Commands;

public sealed record ReplayOnboardingPaymentLinksAdminCommand;

/// <summary>
/// Mantenimiento: vuelve a anunciar, alta por alta, a qué oficina pertenece cada alta pagada y qué recibo tiene,
/// para que PaymentApp complete los pagos viejos que quedaron sin oficina ni recibo. Idempotente: PaymentApp
/// no escribe si el pago ya está vinculado, así que se puede correr más de una vez.
/// </summary>
public static class ReplayOnboardingPaymentLinksAdminHandler
{
    public const int PageSize = 200;

    public static async Task<Result<int>> Handle(
        ReplayOnboardingPaymentLinksAdminCommand command,
        ITenantOnboardingRepository onboardings,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var replayed = 0;
        while (true)
        {
            var page = await onboardings.ListPaymentLinksAsync(replayed, PageSize, ct);
            foreach (var link in page)
            {
                bus.TenantId = link.TenantId.ToString();
                await bus.PublishAsync(
                    new OnboardingPaymentLinkReplayedIntegrationEvent
                    {
                        TenantId = link.TenantId,
                        CorrelationId = correlation.CorrelationId,
                        OnboardingId = link.OnboardingId,
                        CreatedTenantId = link.TenantId,
                        ReceiptFileId = link.ReceiptFileId,
                    }
                );
            }

            replayed += page.Count;
            if (page.Count < PageSize)
                return Result.Success(replayed);
        }
    }
}
