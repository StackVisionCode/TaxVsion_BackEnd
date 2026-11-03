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
    public static IPaymentAdapterFactory FakeAdapters(params RecordingPaymentProvider[] providers)
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

    public Task<Result<ChargeAuthorizationResult>> AuthorizeChargeAsync(
        TenantProviderCredentials credentials,
        ChargeAuthorizationRequest request,
        CancellationToken ct
    )
    {
        Charges.Add(credentials);
        Requests.Add(request);
        var failed = Status == PaymentStatus.Failed;
        return Task.FromResult(
            Result.Success(
                new ChargeAuthorizationResult(
                    $"{Code}-charge-{Charges.Count}",
                    Status,
                    FailureCode: failed ? "card_declined" : null,
                    FailureMessage: failed ? "Declined." : null,
                    Payer: Payer
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
}
