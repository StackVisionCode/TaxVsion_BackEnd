using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Api.Common;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Infrastructure;

namespace TaxVision.PaymentClient.Tests.Api;

/// <summary>URL pública del webhook (base de las opciones + ruta nombrada del enrutado) y validación de
/// las opciones al arrancar.</summary>
public sealed class PaymentWebhookUrlBuilderTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Theory]
    [InlineData("https://api.example.test")]
    [InlineData("https://api.example.test/")]
    public void Joins_the_public_base_with_the_named_route_of_the_office(string baseUrl)
    {
        var builder = Builder(baseUrl);

        var url = builder.Build(PaymentWebhookRoutes.Stripe, TenantId);

        Assert.Equal(new Uri($"https://api.example.test/payments-client/webhooks/{TenantId}/stripe"), url);
    }

    [Fact]
    public void Without_a_public_base_or_an_unknown_route_there_is_no_url()
    {
        Assert.Null(Builder(null).Build(PaymentWebhookRoutes.Stripe, TenantId));
        Assert.Null(Builder("https://api.example.test").Build("UnknownRoute", TenantId));
    }

    [Fact]
    public void The_options_default_to_off_and_bad_values_fail_at_startup()
    {
        var defaults = Resolve([]);
        Assert.False(defaults.IsAutoSetupEnabled);
        Assert.True(defaults.AutoRegister);
        Assert.True(Resolve([new("PublicBaseUrl", "https://api.example.test")]).IsAutoSetupEnabled);
        Assert.False(
            Resolve([new("PublicBaseUrl", "https://api.example.test"), new("AutoRegister", "false")]).IsAutoSetupEnabled
        );

        Assert.Throws<OptionsValidationException>(() => Resolve([new("PublicBaseUrl", "http://api.example.test")]));
        Assert.Throws<OptionsValidationException>(() => Resolve([new("PublicBaseUrl", "api.example.test")]));
        Assert.Throws<OptionsValidationException>(() =>
            Resolve([new("PublicBaseUrl", "https://api.example.test/?x=1")])
        );
        Assert.Throws<OptionsValidationException>(() => Resolve([new("MaxAttempts", "0")]));
        Assert.Throws<OptionsValidationException>(() => Resolve([new("RetryDelaySeconds", "1")]));
        Assert.Throws<OptionsValidationException>(() => Resolve([new("PendingMarginMinutes", "1")]));
        Assert.Throws<OptionsValidationException>(() => Resolve([new("PendingSweepIntervalMinutes", "0")]));
        Assert.Throws<OptionsValidationException>(() => Resolve([new("PendingSweepBatchSize", "0")]));
    }

    private static LinkGeneratorPaymentWebhookUrlBuilder Builder(string? baseUrl) =>
        new(new NamedRouteLinkGenerator(), Options.Create(new PaymentWebhookOptions { PublicBaseUrl = baseUrl }));

    private static PaymentWebhookOptions Resolve(KeyValuePair<string, string?>[] overrides)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                overrides
                    .Select(pair => new KeyValuePair<string, string?>(
                        $"{PaymentWebhookOptions.SectionName}:{pair.Key}",
                        pair.Value
                    ))
                    .Append(new("ConnectionStrings:Default", "Server=unused;Database=unused"))
            )
            .Build();
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddPaymentClientInfrastructure(configuration)
            .BuildServiceProvider();
        return provider.GetRequiredService<IOptions<PaymentWebhookOptions>>().Value;
    }

    /// <summary>Enrutado mínimo: solo conoce la ruta nombrada del webhook de Stripe.</summary>
    private sealed class NamedRouteLinkGenerator : LinkGenerator
    {
        public override string? GetPathByAddress<TAddress>(
            TAddress address,
            RouteValueDictionary values,
            PathString pathBase = default,
            FragmentString fragment = default,
            LinkOptions? options = null
        ) =>
            Equals(address, PaymentWebhookRoutes.Stripe)
                ? $"{pathBase}/payments-client/webhooks/{values["tenantId"]}/stripe"
                : null;

        public override string? GetPathByAddress<TAddress>(
            HttpContext httpContext,
            TAddress address,
            RouteValueDictionary values,
            RouteValueDictionary? ambientValues = null,
            PathString? pathBase = null,
            FragmentString fragment = default,
            LinkOptions? options = null
        ) => throw new NotSupportedException();

        public override string? GetUriByAddress<TAddress>(
            TAddress address,
            RouteValueDictionary values,
            string? scheme,
            HostString host,
            PathString pathBase = default,
            FragmentString fragment = default,
            LinkOptions? options = null
        ) => throw new NotSupportedException();

        public override string? GetUriByAddress<TAddress>(
            HttpContext httpContext,
            TAddress address,
            RouteValueDictionary values,
            RouteValueDictionary? ambientValues = null,
            string? scheme = null,
            HostString? host = null,
            PathString? pathBase = null,
            FragmentString fragment = default,
            LinkOptions? options = null
        ) => throw new NotSupportedException();
    }
}
