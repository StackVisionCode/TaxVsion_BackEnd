using System.Reflection;
using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.PaymentLinks.Commands.PrepareLinkRedemption;
using TaxVision.PaymentClient.Application.PaymentLinks.Commands.RedeemPaymentLink;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ChargeTenantPayment;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ExecuteTenantPaymentCharge;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.PrepareTenantPayment;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.RecordTenantPaymentCharge;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.TenantPayments;
using Wolverine;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>Corre el cobro completo (preparar, llamar al proveedor, registrar) sin Wolverine: los orquestadores piden
/// cada paso por el bus y este bus los resuelve en línea con las dependencias de la prueba.</summary>
internal static class TestChargeFlow
{
    public static Task<Result<Guid>> ChargeAsync(
        ChargeTenantPaymentCommand command,
        ITenantPaymentRepository payments,
        ITenantPaymentConfigRepository configs,
        ITenantConnectAccountRepository connectAccounts,
        IPaymentAdapterFactory providerFactory,
        ISecretProtector secretProtector,
        IOptions<PlatformStripeCredentials> platformCredentials,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentClientMetrics metrics,
        ICorrelationContext correlation,
        ILogger<TenantPayment> logger,
        CancellationToken ct
    )
    {
        IMessageBus bus = null!;
        bus = RoutingBus.Create(message =>
            message switch
            {
                PrepareTenantPaymentCommand prepare => PrepareTenantPaymentHandler.Handle(
                    prepare,
                    payments,
                    configs,
                    connectAccounts,
                    secretProtector,
                    audit,
                    unitOfWork,
                    metrics,
                    correlation,
                    logger,
                    ct
                ),
                ExecuteTenantPaymentChargeCommand execute => ExecuteTenantPaymentChargeHandler.Handle(
                    execute,
                    payments,
                    configs,
                    connectAccounts,
                    providerFactory,
                    secretProtector,
                    platformCredentials,
                    bus,
                    logger,
                    ct
                ),
                RecordTenantPaymentChargeCommand record => RecordTenantPaymentChargeHandler.Handle(
                    record,
                    payments,
                    links: null!,
                    audit,
                    unitOfWork,
                    bus,
                    metrics,
                    correlation,
                    logger,
                    ct
                ),
                _ => throw new NotSupportedException(message.GetType().Name),
            }
        );
        return ChargeTenantPaymentHandler.Handle(command, bus, ct);
    }

    public static Task<Result<RedeemPaymentLinkResponse>> RedeemAsync(
        RedeemPaymentLinkCommand command,
        IPaymentLinkRepository links,
        ITenantPaymentRepository payments,
        ITenantPaymentConfigRepository configs,
        IPaymentAdapterFactory providerFactory,
        ISecretProtector secretProtector,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IMessageBus publishing,
        IPaymentClientMetrics metrics,
        ICorrelationContext correlation,
        ILogger<PaymentLink> logger,
        CancellationToken ct
    )
    {
        var platform = Options.Create(
            new PlatformStripeCredentials { PlatformSecretKey = "unused", ConnectWebhookSecret = "unused" }
        );
        IMessageBus bus = null!;
        bus = RoutingBus.Create(message =>
            message switch
            {
                PrepareLinkRedemptionCommand prepare => PrepareLinkRedemptionHandler.Handle(
                    prepare,
                    links,
                    payments,
                    configs,
                    providerFactory,
                    secretProtector,
                    audit,
                    unitOfWork,
                    correlation,
                    logger,
                    ct
                ),
                ExecuteTenantPaymentChargeCommand execute => ExecuteTenantPaymentChargeHandler.Handle(
                    execute,
                    payments,
                    configs,
                    connectAccounts: null!,
                    providerFactory,
                    secretProtector,
                    platform,
                    bus,
                    NullLogger<TenantPayment>.Instance,
                    ct
                ),
                RecordTenantPaymentChargeCommand record => RecordTenantPaymentChargeHandler.Handle(
                    record,
                    payments,
                    links,
                    audit,
                    unitOfWork,
                    publishing,
                    metrics,
                    correlation,
                    NullLogger<TenantPayment>.Instance,
                    ct
                ),
                _ => throw new NotSupportedException(message.GetType().Name),
            }
        );
        return RedeemPaymentLinkHandler.Handle(command, bus, ct);
    }

    private class RoutingBus : DispatchProxy
    {
        private Func<object, object> _route = null!;

        public static IMessageBus Create(Func<object, object> route)
        {
            var bus = Create<IMessageBus, RoutingBus>();
            ((RoutingBus)(object)bus)._route = route;
            return bus;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                "set_TenantId" or "get_TenantId" => null,
                nameof(IMessageBus.InvokeAsync) => _route(args![0]!),
                _ => throw new NotSupportedException(targetMethod?.Name),
            };
    }
}
