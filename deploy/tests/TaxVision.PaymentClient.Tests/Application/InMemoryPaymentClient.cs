using System.Reflection;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Providers;
using Wolverine;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>Base en memoria con los repositorios reales, para probar los handlers de configuración y
/// cobro de punta a punta sin SQL Server ni proveedores reales.</summary>
internal static class InMemoryPaymentClient
{
    public static PaymentClientDbContext CreateDb() => CreateDb(Guid.NewGuid().ToString());

    /// <summary>Mismo nombre = misma base: permite releer con un contexto nuevo (sin caché de tracking).</summary>
    public static PaymentClientDbContext CreateDb(string databaseName) =>
        new(
            new DbContextOptionsBuilder<PaymentClientDbContext>().UseInMemoryDatabase(databaseName).Options,
            new NoTenantContext()
        );

    /// <summary>Fábrica real con los adaptadores reales (Stripe y PayPal) registrados por reflexión.</summary>
    public static IPaymentAdapterFactory RealAdapters() =>
        new ServiceCollection()
            .AddLogging()
            .AddPaymentProviders()
            .BuildServiceProvider()
            .GetRequiredService<IPaymentAdapterFactory>();

    /// <summary>Fábrica real con adaptadores falsos que registran cada cobro.</summary>
    public static IPaymentAdapterFactory FakeAdapters(params IPaymentProvider[] providers)
    {
        var services = new ServiceCollection();
        foreach (var provider in providers)
            services.AddKeyedSingleton<IPaymentProvider>(provider.Code, provider);

        return new KeyedPaymentAdapterFactory(
            services.BuildServiceProvider(),
            providers.Select(provider => provider.Code).ToList()
        );
    }

    /// <summary>Bus que solo registra lo publicado (el resto de IMessageBus no se usa en estos handlers).</summary>
    public static IMessageBus RecordingBus(List<object> published)
    {
        var bus = DispatchProxy.Create<IMessageBus, RecordingBusProxy>();
        ((RecordingBusProxy)(object)bus).Published = published;
        return bus;
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}

internal class RecordingBusProxy : DispatchProxy
{
    public List<object> Published { get; set; } = [];

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        // El tenant que estampan los jobs antes de publicar no afecta lo que se registra.
        if (targetMethod?.Name is "set_TenantId" or "get_TenantId")
            return null;

        if (targetMethod?.Name != nameof(IMessageBus.PublishAsync))
            throw new NotSupportedException(targetMethod?.Name);

        Published.Add(args![0]!);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Adaptador falso: siempre aprueba y anota con qué credenciales cobró.</summary>
internal sealed class RecordingPaymentProvider(PaymentProviderDescriptor descriptor) : IPaymentProvider
{
    public List<TenantProviderCredentials> Charges { get; } = [];

    public PaymentProviderCode Code => descriptor.Code;

    public PaymentProviderDescriptor Descriptor => descriptor;

    /// <summary>Pedidos recibidos (para ver qué ReceiptEmail llegó al proveedor).</summary>
    public List<ChargeAuthorizationRequest> Requests { get; } = [];

    /// <summary>Pagador que devuelve el cobro; null = el proveedor no informa nada.</summary>
    public ProviderPayerDetails? Payer { get; set; }

    /// <summary>Estado con el que responde el cobro.</summary>
    public PaymentStatus Status { get; set; } = PaymentStatus.Succeeded;

    /// <summary>URL de verificación (3DS) que devuelve el cobro cuando queda en RequiresAction.</summary>
    public string? NextActionUrl { get; set; }

    /// <summary>clientSecret del 3DS con Stripe.js (use_stripe_sdk) cuando queda en RequiresAction.</summary>
    public string? ClientSecret { get; set; }

    public Task<Result<ChargeAuthorizationResult>> AuthorizeChargeAsync(
        TenantProviderCredentials credentials,
        ChargeAuthorizationRequest request,
        CancellationToken ct
    )
    {
        Charges.Add(credentials);
        Requests.Add(request);
        var failed = Status == PaymentStatus.Failed;
        var requiresAction = Status == PaymentStatus.RequiresAction && (NextActionUrl ?? ClientSecret) is not null;
        return Task.FromResult(
            Result.Success(
                new ChargeAuthorizationResult(
                    $"{Code}-charge-{Charges.Count}",
                    Status,
                    NextActionType: requiresAction
                        ? (NextActionUrl is null ? "use_stripe_sdk" : "redirect_to_url")
                        : null,
                    NextActionUrl: requiresAction ? NextActionUrl : null,
                    FailureCode: failed ? "card_declined" : null,
                    FailureMessage: failed ? "Declined." : null,
                    Payer: Payer,
                    NextActionClientSecret: requiresAction ? ClientSecret : null
                )
            )
        );
    }

    public Task<Result<RefundResult>> RefundAsync(
        TenantProviderCredentials credentials,
        string providerChargeReference,
        Money amount,
        string reason,
        CancellationToken ct
    ) => throw new NotSupportedException();

    /// <summary>Webhook ya verificado y su payload; sin configurar, lanza (el test no lo esperaba).</summary>
    public (WebhookVerificationResult Verification, WebhookEventPayload Payload)? Webhook { get; set; }

    public Task<Result<WebhookVerificationResult>> VerifyWebhookSignatureAsync(
        string rawPayload,
        IReadOnlyDictionary<string, string> headers,
        TenantProviderCredentials credentials,
        CancellationToken ct
    ) => Task.FromResult(Result.Success((Webhook ?? throw new NotSupportedException()).Verification));

    public Task<Result<WebhookEventPayload>> ParseWebhookEventAsync(
        string rawPayload,
        string eventType,
        CancellationToken ct
    ) => Task.FromResult(Result.Success((Webhook ?? throw new NotSupportedException()).Payload));

    /// <summary>Consultas de estado recibidas (credenciales + cargo).</summary>
    public List<(TenantProviderCredentials Credentials, ChargeStatusLookup Lookup)> StatusLookups { get; } = [];

    /// <summary>Respuesta de la consulta de estado; sin configurar, lanza (el test no la esperaba).</summary>
    public Func<ChargeStatusLookup, Result<WebhookEventPayload>>? ChargeStatus { get; set; }

    public Task<Result<WebhookEventPayload>> GetChargeStatusAsync(
        TenantProviderCredentials credentials,
        ChargeStatusLookup lookup,
        CancellationToken ct
    )
    {
        StatusLookups.Add((credentials, lookup));
        return Task.FromResult(
            (ChargeStatus ?? throw new InvalidOperationException("Unexpected charge status lookup."))(lookup)
        );
    }

    /// <summary>Búsquedas recibidas de pagos sin resultado.</summary>
    public List<ChargeSearch> Searches { get; } = [];

    /// <summary>Respuesta de la búsqueda; sin configurar, lanza (el test no la esperaba).</summary>
    public Func<ChargeSearch, Result<ChargeAuthorizationResult?>>? FoundCharge { get; set; }

    public Task<Result<ChargeAuthorizationResult?>> FindChargeAsync(
        TenantProviderCredentials credentials,
        ChargeSearch search,
        CancellationToken ct
    )
    {
        Searches.Add(search);
        return Task.FromResult(
            (FoundCharge ?? throw new InvalidOperationException("Unexpected charge search."))(search)
        );
    }

    /// <summary>Cancelaciones pedidas al proveedor (credenciales + cargo).</summary>
    public List<(TenantProviderCredentials Credentials, ChargeStatusLookup Lookup)> Cancellations { get; } = [];

    /// <summary>Respuesta de la cancelación; sin configurar, lanza (el test no la esperaba).</summary>
    public Func<ChargeStatusLookup, Result<WebhookEventPayload>>? CancelCharge { get; set; }

    public Task<Result<WebhookEventPayload>> CancelChargeAsync(
        TenantProviderCredentials credentials,
        ChargeStatusLookup lookup,
        CancellationToken ct
    )
    {
        Cancellations.Add((credentials, lookup));
        return Task.FromResult(
            (CancelCharge ?? throw new InvalidOperationException("Unexpected charge cancellation."))(lookup)
        );
    }
}

/// <summary>Adaptador falso idempotente como Stripe: la misma clave devuelve el mismo cargo y no cobra
/// de nuevo. Seguro para canjes en paralelo.</summary>
internal sealed class IdempotentRecordingPaymentProvider(PaymentProviderDescriptor descriptor) : IPaymentProvider
{
    private readonly Dictionary<string, ChargeAuthorizationResult> _byKey = [];
    private readonly Lock _gate = new();

    public PaymentProviderCode Code => descriptor.Code;

    public PaymentProviderDescriptor Descriptor => descriptor;

    public PaymentStatus Status { get; set; } = PaymentStatus.Succeeded;

    /// <summary>Claves distintas que llegaron a cobrarse.</summary>
    public int DistinctChargedKeys
    {
        get
        {
            lock (_gate)
                return _byKey.Count;
        }
    }

    /// <summary>Si es mayor que 1, cada cobro espera a que lleguen esos llamados (fuerza la carrera).</summary>
    public int WaitForCallers { get; set; }

    /// <summary>Corre al recibir cada cobro: sirve para mirar la base desde otro contexto en ese momento.</summary>
    public Func<ChargeAuthorizationRequest, Task>? OnCharge { get; set; }

    /// <summary>Llamadas que llegaron al proveedor.</summary>
    public int Calls => _totalCalls;

    /// <summary>Si es true, la búsqueda responde que no se pudo saber.</summary>
    public bool SearchUnavailable { get; set; }

    private int _calls;
    private int _totalCalls;
    private readonly Dictionary<string, ChargeAuthorizationResult> _byPaymentId = [];
    private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<Result<ChargeAuthorizationResult>> AuthorizeChargeAsync(
        TenantProviderCredentials credentials,
        ChargeAuthorizationRequest request,
        CancellationToken ct
    )
    {
        Interlocked.Increment(ref _totalCalls);
        if (OnCharge is not null)
            await OnCharge(request);

        if (WaitForCallers > 1)
        {
            if (Interlocked.Increment(ref _calls) >= WaitForCallers)
                _allArrived.TrySetResult();
            await _allArrived.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        }

        lock (_gate)
        {
            if (!_byKey.TryGetValue(request.IdempotencyKey.Value, out var result))
            {
                result = new ChargeAuthorizationResult($"{Code}-charge-{Guid.NewGuid():N}", Status);
                _byKey[request.IdempotencyKey.Value] = result;
                _byPaymentId[request.Metadata["tenantPaymentId"]] = result;
            }

            return Result.Success(result);
        }
    }

    public Task<Result<ChargeAuthorizationResult?>> FindChargeAsync(
        TenantProviderCredentials credentials,
        ChargeSearch search,
        CancellationToken ct
    )
    {
        if (SearchUnavailable)
            return Task.FromResult(
                Result.Failure<ChargeAuthorizationResult?>(new Error("Fake.ChargeSearch.Failed", "Search unavailable."))
            );

        lock (_gate)
            return Task.FromResult(
                Result.Success<ChargeAuthorizationResult?>(
                    _byPaymentId.GetValueOrDefault(search.TenantPaymentId.ToString("N"))
                )
            );
    }

    public Task<Result<RefundResult>> RefundAsync(
        TenantProviderCredentials credentials,
        string providerChargeReference,
        Money amount,
        string reason,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<Result<WebhookVerificationResult>> VerifyWebhookSignatureAsync(
        string rawPayload,
        IReadOnlyDictionary<string, string> headers,
        TenantProviderCredentials credentials,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<Result<WebhookEventPayload>> ParseWebhookEventAsync(
        string rawPayload,
        string eventType,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<Result<WebhookEventPayload>> GetChargeStatusAsync(
        TenantProviderCredentials credentials,
        ChargeStatusLookup lookup,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<Result<WebhookEventPayload>> CancelChargeAsync(
        TenantProviderCredentials credentials,
        ChargeStatusLookup lookup,
        CancellationToken ct
    ) => throw new NotSupportedException();
}

/// <summary>"Cifra" con un prefijo reversible: suficiente para probar el recorrido sin claves reales.</summary>
internal sealed class PrefixSecretProtector : ISecretProtector
{
    private const string Prefix = "enc:";

    public string Protect(string plaintext) => Prefix + plaintext;

    public bool TryUnprotect(string? protectedValue, out string plaintext, out SecretUnprotectFailure failure)
    {
        failure = default;
        plaintext =
            protectedValue is not null && protectedValue.StartsWith(Prefix, StringComparison.Ordinal)
                ? protectedValue[Prefix.Length..]
                : string.Empty;
        return plaintext.Length > 0;
    }
}

internal sealed class FixedCorrelationContext : ICorrelationContext
{
    public string CorrelationId => "test-correlation-id";

    public void Set(string correlationId) { }

    public IDisposable Push(string correlationId) => new NoOpDisposable();

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

internal sealed class NoOpPaymentClientMetrics : IPaymentClientMetrics
{
    public void RecordPaymentSucceeded(long amountCents, string currency) { }

    public void RecordPlatformFee(long feeCents, string currency) { }

    public void RecordConnectOnboardingCompleted() { }

    public void RecordPaymentLinkCreated() { }

    public void RecordPaymentLinkUsed() { }

    public void RecordRefund(string provider) { }

    public void RecordWebhookReceived(string provider) { }

    public void RecordWebhookDuplicate(string provider) { }

    public void RecordWebhookSignatureFailed(string provider) { }

    public void RecordWebhookNeedsReview(string provider, string reason) { }
}
