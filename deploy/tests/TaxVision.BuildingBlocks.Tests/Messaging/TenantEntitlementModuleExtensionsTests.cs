using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Messaging;

/// <summary>
/// Extracción de módulos habilitados del snapshot de entitlements (fundación del gate de módulo):
/// solo cuentan las claves <c>module.*</c> con valor <c>true</c>; el resto se ignora.
/// </summary>
public sealed class TenantEntitlementModuleExtensionsTests
{
    private static TenantEntitlementsChangedIntegrationEvent EventWith(IReadOnlyDictionary<string, string> values) =>
        new()
        {
            RevisionNumber = 1,
            ChangedKeys = [],
            PlanCode = "pro",
            SubscriptionStatus = "Active",
            SeatCount = 5,
            AvailableSeatCount = 5,
            EntitlementValues = values,
        };

    [Fact]
    public void Extracts_only_enabled_modules_stripping_the_prefix()
    {
        var evt = EventWith(
            new Dictionary<string, string>
            {
                ["module.signatures"] = "True",
                ["module.documents"] = "true",
                ["module.comms"] = "False", // deshabilitado → excluido
                ["seats.max"] = "15", // no es módulo → ignorado
                ["storage.max_bytes"] = "107374182400",
            }
        );

        var modules = evt.ExtractEnabledModules();

        Assert.Contains("signatures", modules);
        Assert.Contains("documents", modules);
        Assert.DoesNotContain("comms", modules);
        Assert.DoesNotContain("seats.max", modules);
        Assert.Equal(2, modules.Length);
    }

    [Fact]
    public void Empty_when_no_modules_present()
    {
        var evt = EventWith(new Dictionary<string, string> { ["seats.max"] = "10" });
        Assert.Empty(evt.ExtractEnabledModules());
    }

    [Fact]
    public void Ignores_non_boolean_module_values()
    {
        var evt = EventWith(new Dictionary<string, string> { ["module.signatures"] = "yes" }); // no parsea a bool
        Assert.Empty(evt.ExtractEnabledModules());
    }
}
