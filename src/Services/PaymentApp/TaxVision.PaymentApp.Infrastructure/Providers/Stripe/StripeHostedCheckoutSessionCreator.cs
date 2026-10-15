using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.ValueObjects;
// Alias explícito: este archivo vive en el namespace ...Providers.Stripe, así que una referencia
// calificada "Stripe.Checkout.X" se resuelve como relativa al namespace propio en vez del namespace
// global del SDK (mismo motivo que en StripePaymentAdapter).
using CheckoutSessionCreateOptions = Stripe.Checkout.SessionCreateOptions;
using CheckoutSessionCustomerUpdateOptions = Stripe.Checkout.SessionCustomerUpdateOptions;
using CheckoutSessionService = Stripe.Checkout.SessionService;
using CheckoutSessionSetupIntentDataOptions = Stripe.Checkout.SessionSetupIntentDataOptions;

namespace TaxVision.PaymentApp.Infrastructure.Providers.Stripe;

/// <summary>
/// Adaptador provider-agnostic → Stripe para abrir el Checkout con guardado de método.
/// Resuelve (o provisiona lazy) el <see cref="TenantProviderCustomer"/> local, pasa su
/// <c>cus_</c> al Checkout y traduce la respuesta cruda del adapter a
/// <see cref="HostedCheckoutSession"/>.
/// </summary>
public sealed class StripeHostedCheckoutSessionCreator : IHostedCheckoutSessionCreator
{
    private const int ExpiryHours = 24;

    private readonly IPaymentAdapterFactory _adapters;
    private readonly ITenantProviderCustomerRepository _customers;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOptions<StripeOptions> _stripeOptions;
    private readonly ILogger<StripeHostedCheckoutSessionCreator> _logger;

    public StripeHostedCheckoutSessionCreator(
        IPaymentAdapterFactory adapters,
        ITenantProviderCustomerRepository customers,
        IUnitOfWork unitOfWork,
        IOptions<StripeOptions> stripeOptions,
        ILogger<StripeHostedCheckoutSessionCreator> logger
    )
    {
        _adapters = adapters;
        _customers = customers;
        _unitOfWork = unitOfWork;
        _stripeOptions = stripeOptions;
        _logger = logger;
    }

    public string ProviderCode => PaymentProviderCode.Stripe.ToString();

    public async Task<Result<HostedCheckoutSession>> CreateAsync(
        HostedCheckoutCreateRequest request,
        CancellationToken ct
    )
    {
        IPaymentProvider adapter;
        try
        {
            adapter = _adapters.Resolve(PaymentProviderCode.Stripe);
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<HostedCheckoutSession>(
                new Error("PaymentProvider.NotConfigured", "Stripe is not configured in this environment.")
            );
        }

        if (request.IntentType == HostedCheckoutIntentType.Setup)
            return await CreateSetupSessionAsync(request, adapter, ct);

        var customerResult = await ResolveStripeCustomerAsync(request, adapter, ct);
        if (customerResult.IsFailure)
            return Result.Failure<HostedCheckoutSession>(customerResult.Error);

        var amountResult = Money.Create(request.AmountCents, request.Currency);
        if (amountResult.IsFailure)
            return Result.Failure<HostedCheckoutSession>(amountResult.Error);

        var idemResult = IdempotencyKey.Create(request.IdempotencyKey);
        if (idemResult.IsFailure)
            return Result.Failure<HostedCheckoutSession>(idemResult.Error);

        // Descriptor conservador — este flujo lo invoca el landing con un plan ya elegido,
        // así que un literal corto satisface a Stripe (<= 22 chars) y evita acoplar al nombre
        // del plan que todavía no viajó en el request.
        var descriptorResult = StatementDescriptor.Create("SUBSCRIPTION");
        if (descriptorResult.IsFailure)
            return Result.Failure<HostedCheckoutSession>(descriptorResult.Error);

        var method = ParseMethod(request.Method);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenantId"] = request.TenantId.ToString("N"),
            ["saaSPaymentId"] = request.SaaSPaymentId.ToString("N"),
        };

        var sessionRequest = new HostedCheckoutSessionRequest(
            Amount: amountResult.Value,
            Method: method,
            IdempotencyKey: idemResult.Value,
            Descriptor: descriptorResult.Value,
            PayerEmail: request.PayerEmail,
            SuccessUrl: request.SuccessUrl,
            CancelUrl: request.CancelUrl,
            ExpiresAtUtc: DateTime.UtcNow.AddHours(ExpiryHours),
            Metadata: metadata,
            StoreMethodForOffSession: request.StoreMethodForOffSession,
            ProviderCustomerId: customerResult.Value
        );

        var sessionResult = await adapter.CreateHostedCheckoutSessionAsync(sessionRequest, ct);
        if (sessionResult.IsFailure)
            return Result.Failure<HostedCheckoutSession>(sessionResult.Error);

        return Result.Success(
            new HostedCheckoutSession(
                PaymentId: request.SaaSPaymentId,
                CheckoutUrl: sessionResult.Value.CheckoutUrl,
                ExpiresAtUtc: sessionRequest.ExpiresAtUtc
            )
        );
    }

    /// <summary>Devuelve el <c>cus_</c> de Stripe cuando el caller pidió guardado off-session.
    /// Si no hay <see cref="TenantProviderCustomer"/> local, lo provisiona lazy via el adapter.
    /// Si el caller no pidió guardado, devuelve null — el adapter cae al modo CustomerEmail.</summary>
    private async Task<Result<string?>> ResolveStripeCustomerAsync(
        HostedCheckoutCreateRequest request,
        IPaymentProvider adapter,
        CancellationToken ct
    )
    {
        if (!request.StoreMethodForOffSession)
            return Result.Success<string?>(null);

        var existing = await _customers.GetByTenantAndProviderAsync(request.TenantId, PaymentProviderCode.Stripe, ct);
        if (existing is not null)
            return Result.Success<string?>(existing.CustomerReference.Value);

        var tokenResult = await adapter.GetOrCreateCustomerAsync(request.TenantId, request.PayerEmail, name: null, ct);
        if (tokenResult.IsFailure)
            return Result.Failure<string?>(tokenResult.Error);

        var referenceResult = ProviderCustomerReference.Create(PaymentProviderCode.Stripe, tokenResult.Value.Token);
        if (referenceResult.IsFailure)
            return Result.Failure<string?>(referenceResult.Error);

        var registered = TenantProviderCustomer.Register(
            request.TenantId,
            PaymentProviderCode.Stripe,
            referenceResult.Value,
            request.PayerEmail,
            DateTime.UtcNow
        );
        if (registered.IsFailure)
            return Result.Failure<string?>(registered.Error);

        await _customers.AddAsync(registered.Value, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Provisioned Stripe customer {Customer} lazily for tenant {TenantId}.",
            tokenResult.Value.Token,
            request.TenantId
        );

        return Result.Success<string?>(tokenResult.Value.Token);
    }

    private static PaymentMethodKind ParseMethod(string? method)
    {
        if (string.IsNullOrWhiteSpace(method))
            return PaymentMethodKind.Card;

        return Enum.TryParse<PaymentMethodKind>(method, ignoreCase: true, out var parsed)
            ? parsed
            : PaymentMethodKind.Card;
    }

    /// <summary>Setup-only: NO cobra. Crea una Checkout Session en modo "setup" que recoge una tarjeta y la
    /// deja chargeable off-session para el customer. Siempre resuelve/provisiona el cus_ (sin él, el
    /// SetupIntent de Stripe no puede atribuir el método al tenant). El webhook
    /// <c>checkout.session.completed</c> con <c>mode=setup</c> adjunta el método localmente y reemplaza el
    /// default existente.</summary>
    private async Task<Result<HostedCheckoutSession>> CreateSetupSessionAsync(
        HostedCheckoutCreateRequest request,
        IPaymentProvider adapter,
        CancellationToken ct
    )
    {
        var setupRequest = request with { StoreMethodForOffSession = true };
        var customerResult = await ResolveStripeCustomerAsync(setupRequest, adapter, ct);
        if (customerResult.IsFailure)
            return Result.Failure<HostedCheckoutSession>(customerResult.Error);
        if (string.IsNullOrEmpty(customerResult.Value))
            return Result.Failure<HostedCheckoutSession>(
                new Error(
                    "Stripe.SetupSession.MissingCustomer",
                    "A Stripe customer is required to open a setup-only checkout."
                )
            );

        var idemResult = IdempotencyKey.Create(request.IdempotencyKey);
        if (idemResult.IsFailure)
            return Result.Failure<HostedCheckoutSession>(idemResult.Error);

        var expiresAtUtc = DateTime.UtcNow.AddHours(ExpiryHours);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenantId"] = request.TenantId.ToString("N"),
            ["intent"] = "setup",
        };

        var options = new CheckoutSessionCreateOptions
        {
            Mode = "setup",
            PaymentMethodTypes = ["card"],
            Customer = customerResult.Value,
            CustomerUpdate = new CheckoutSessionCustomerUpdateOptions { Address = "auto" },
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            ExpiresAt = expiresAtUtc,
            Metadata = metadata,
            SetupIntentData = new CheckoutSessionSetupIntentDataOptions { Metadata = metadata },
        };

        try
        {
            var service = new CheckoutSessionService(new StripeClient(_stripeOptions.Value.SecretKey));
            var session = await service.CreateAsync(
                options,
                new RequestOptions { IdempotencyKey = idemResult.Value.Value },
                ct
            );

            return Result.Success(new HostedCheckoutSession(Guid.Empty, session.Url, expiresAtUtc));
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(
                ex,
                "Stripe setup Checkout Session creation failed for tenant {TenantId}: {Error}",
                request.TenantId,
                ex.StripeError?.Message ?? ex.Message
            );
            return Result.Failure<HostedCheckoutSession>(
                new Error("Stripe.SetupSession.Failed", ex.StripeError?.Message ?? ex.Message)
            );
        }
    }
}
