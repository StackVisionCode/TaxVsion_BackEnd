using System.Text.RegularExpressions;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Messaging;

/// <summary>Guardrails de contrato específicos del evento Automatic/Manual:
/// (a) Subscription lo rutea al exchange compartido, (b) PaymentApp lo consume con un
/// handler discoverable y lo registra explícitamente en su Wolverine. Si cualquiera de
/// las dos cosas falta, el flow entero (switch Automatic → off-session) se cae en
/// silencio: el productor cree que publicó y el proyector nunca ve el evento.</summary>
public sealed class TenantBillingPreferenceContractTests
{
    private static DirectoryInfo RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!;
    }

    private static string Read(string relative)
    {
        var path = Path.Combine(RepositoryRoot().FullName, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Not found: {relative}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Subscription_api_routes_the_integration_event_to_the_rabbit_exchange()
    {
        var program = Read("src/Services/Subscription/TaxVision.Subscription.Api/Program.cs");
        var pattern = new Regex(
            @"PublishMessage\s*<\s*(?:[\w.]*\.)?TenantBillingPreferenceChangedIntegrationEvent\s*>\s*\(\s*\)\s*\.\s*ToRabbitExchange\s*\(\s*""taxvision-events""\s*\)",
            RegexOptions.Compiled
        );
        Assert.True(
            pattern.IsMatch(program),
            "Subscription.Api/Program.cs no registra PublishMessage<TenantBillingPreferenceChangedIntegrationEvent>().ToRabbitExchange(\"taxvision-events\"). "
                + "Sin esa ruta, Wolverine entrega el evento en proceso y nunca sale al bus — "
                + "la proyección de PaymentApp nunca lo ve y el flow Automatic queda roto en silencio."
        );
    }

    [Fact]
    public void PaymentApp_api_registers_the_projection_consumer_explicitly()
    {
        var program = Read("src/Services/PaymentApp/TaxVision.PaymentApp.Api/Program.cs");
        Assert.Contains("TenantBillingPreferenceProjectionConsumer", program, StringComparison.Ordinal);
        var pattern = new Regex(
            @"Discovery\s*\.\s*IncludeType\s*\(\s*typeof\s*\(\s*[\w.]*TenantBillingPreferenceProjectionConsumer\s*\)\s*\)",
            RegexOptions.Compiled
        );
        Assert.True(
            pattern.IsMatch(program),
            "PaymentApp.Api/Program.cs no fuerza Discovery.IncludeType(typeof(TenantBillingPreferenceProjectionConsumer)). "
                + "Sin el include explícito, Wolverine puede no engancharlo (ya pasó con otros consumers en PaymentApp)."
        );
    }

    [Fact]
    public void Projection_consumer_handle_signature_matches_the_integration_event()
    {
        var consumer = Read(
            "src/Services/PaymentApp/TaxVision.PaymentApp.Application/Billing/Consumers/TenantBillingPreferenceProjectionConsumer.cs"
        );
        var pattern = new Regex(
            @"public\s+static\s+async\s+Task\s+Handle\s*\(\s*TenantBillingPreferenceChangedIntegrationEvent\b",
            RegexOptions.Compiled
        );
        Assert.True(
            pattern.IsMatch(consumer),
            "La firma de TenantBillingPreferenceProjectionConsumer.Handle debe empezar con TenantBillingPreferenceChangedIntegrationEvent "
                + "— es lo que Wolverine mira para ruteo convencional y lo que el mapa de consumo del repo scrapea."
        );
    }

    [Fact]
    public void Event_payload_carries_the_fields_the_projection_consumes()
    {
        // Si alguien quita un campo del evento, los call sites del consumer ya no compilan,
        // pero conviene documentar explícitamente la forma del contrato público.
        var evt = new TenantBillingPreferenceChangedIntegrationEvent
        {
            TenantId = Guid.NewGuid(),
            Mode = "Automatic",
            HasActiveMandate = true,
            RevisionTicks = 1,
            ChangedAtUtc = DateTime.UtcNow,
        };

        Assert.Equal("Automatic", evt.Mode);
        Assert.True(evt.HasActiveMandate);
        Assert.True(evt.RevisionTicks > 0);
    }
}
