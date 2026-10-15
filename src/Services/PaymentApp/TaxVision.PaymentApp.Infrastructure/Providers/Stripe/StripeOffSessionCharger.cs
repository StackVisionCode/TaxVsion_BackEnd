using System.Diagnostics;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Infrastructure.Providers.Stripe;

/// <summary>
/// Cobro recurrente contra una tarjeta ya guardada (pm_...) adjunta a un Customer.
/// <see cref="OffSession"/>=true + <see cref="PaymentIntentCreateOptions.Confirm"/>=true forman
/// el patrón "merchant initiated" que Stripe documenta; <see cref="OffSessionChargeStatus.RequiresUserAction"/>
/// cubre el caso PSD2/SCA en el que el banco pide 3DS y nos obliga a caer a flujo manual.
/// </summary>
public sealed class StripeOffSessionCharger : IOffSessionCharger
{
    private readonly ITenantProviderCustomerRepository _customers;
    private readonly ILogger<StripeOffSessionCharger> _logger;
    private readonly IPaymentAppMetrics _metrics;
    private readonly Func<PaymentIntentCreateOptions, RequestOptions, CancellationToken, Task<PaymentIntent>> _create;

    public StripeOffSessionCharger(
        IOptions<StripeOptions> options,
        ITenantProviderCustomerRepository customers,
        ILogger<StripeOffSessionCharger> logger,
        IPaymentAppMetrics metrics
    )
    {
        _customers = customers;
        _logger = logger;
        _metrics = metrics;
        var client = new StripeClient(options.Value.SecretKey);
        var service = new PaymentIntentService(client);
        _create = (opts, reqOpts, ct) => service.CreateAsync(opts, reqOpts, ct);
    }

    // Seam de tests: inyecta un fake sin crear un StripeClient real ni tocar la red.
    public StripeOffSessionCharger(
        Func<PaymentIntentCreateOptions, RequestOptions, CancellationToken, Task<PaymentIntent>> create,
        ITenantProviderCustomerRepository customers,
        ILogger<StripeOffSessionCharger> logger,
        IPaymentAppMetrics metrics
    )
    {
        _customers = customers;
        _logger = logger;
        _metrics = metrics;
        _create = create;
    }

    public string ProviderCode => PaymentProviderCode.Stripe.ToString();

    public async Task<Result<OffSessionChargeOutcome>> ChargeAsync(
        OffSessionChargeRequest request,
        CancellationToken ct
    )
    {
        var customer = await _customers.GetByTenantAndProviderAsync(request.TenantId, PaymentProviderCode.Stripe, ct);
        if (customer is null)
            return Result.Failure<OffSessionChargeOutcome>(
                new Error(
                    "Stripe.OffSession.CustomerMissing",
                    "No Stripe customer is registered for this tenant; a hosted checkout with StoreMethodForOffSession is required first."
                )
            );

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var intent = await _create(
                new PaymentIntentCreateOptions
                {
                    Amount = request.AmountCents,
                    Currency = request.Currency.ToLowerInvariant(),
                    Customer = customer.CustomerReference.Value,
                    PaymentMethod = request.ProviderToken,
                    PaymentMethodTypes = ["card"],
                    Confirm = true,
                    OffSession = true,
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["tenantId"] = request.TenantId.ToString("N"),
                        ["saaSPaymentId"] = request.SaaSPaymentId.ToString("N"),
                    },
                },
                new RequestOptions { IdempotencyKey = request.IdempotencyKey },
                ct
            );

            return Result.Success(MapIntent(intent));
        }
        catch (StripeException ex)
        {
            return Result.Success(MapException(ex));
        }
        finally
        {
            _metrics.RecordProviderLatency(
                stopwatch.Elapsed.TotalMilliseconds,
                PaymentProviderCode.Stripe.ToString(),
                nameof(ChargeAsync)
            );
        }
    }

    private static OffSessionChargeOutcome MapIntent(PaymentIntent intent) =>
        intent.Status switch
        {
            "succeeded" => new OffSessionChargeOutcome(
                OffSessionChargeStatus.Succeeded,
                intent.Id,
                FailureCode: null,
                FailureMessage: null
            ),
            "requires_action" or "requires_source_action" or "requires_payment_method" => new OffSessionChargeOutcome(
                OffSessionChargeStatus.RequiresUserAction,
                intent.Id,
                FailureCode: intent.LastPaymentError?.Code ?? "authentication_required",
                FailureMessage: intent.LastPaymentError?.Message
                    ?? "Issuing bank requires customer authentication; retry with an on-session flow."
            ),
            _ => new OffSessionChargeOutcome(
                OffSessionChargeStatus.Failed,
                intent.Id,
                FailureCode: intent.LastPaymentError?.Code ?? intent.Status,
                FailureMessage: intent.LastPaymentError?.Message
            ),
        };

    private OffSessionChargeOutcome MapException(StripeException ex)
    {
        var code = ex.StripeError?.Code;
        var intentId = ex.StripeError?.PaymentIntent?.Id ?? string.Empty;

        // Stripe documenta "authentication_required" como el signal exacto para caer a on-session.
        if (string.Equals(code, "authentication_required", StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Stripe off-session charge requires SCA for PI {Intent}; falling back to on-session.",
                intentId
            );
            return new OffSessionChargeOutcome(
                OffSessionChargeStatus.RequiresUserAction,
                string.IsNullOrEmpty(intentId) ? null : intentId,
                FailureCode: code,
                FailureMessage: ex.StripeError?.Message ?? ex.Message
            );
        }

        _logger.LogWarning(ex, "Stripe off-session charge failed. IntentHint={Intent}", intentId);
        return new OffSessionChargeOutcome(
            OffSessionChargeStatus.Failed,
            string.IsNullOrEmpty(intentId) ? null : intentId,
            FailureCode: code ?? "stripe_error",
            FailureMessage: ex.StripeError?.Message ?? ex.Message
        );
    }
}
