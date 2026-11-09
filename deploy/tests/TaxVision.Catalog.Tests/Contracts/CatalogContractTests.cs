using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BuildingBlocks.Messaging.CatalogIntegrationEvents;
using Wolverine.Attributes;

namespace TaxVision.Catalog.Tests.Contracts;

/// <summary>CAT-1: el contrato solo crece. Un mensaje en vuelo sin los campos nuevos se sigue leyendo.</summary>
public sealed class CatalogContractTests
{
    [Fact]
    public void TrackingChanged_has_a_versioned_message_identity()
    {
        var identity =
            typeof(CatalogItemTrackingChangedIntegrationEvent).GetCustomAttribute<MessageIdentityAttribute>();

        Assert.Equal("catalog.item.tracking_changed.v1", identity?.Alias);
    }

    [Theory]
    [InlineData(typeof(CatalogItemUpdatedIntegrationEvent), "TrackInventory")]
    [InlineData(typeof(CatalogItemUpdatedIntegrationEvent), "IsActive")]
    [InlineData(typeof(CatalogItemDeactivatedIntegrationEvent), "TrackInventory")]
    public void New_fields_on_existing_events_are_optional(Type type, string name)
    {
        var property = type.GetProperty(name);

        Assert.NotNull(property);
        Assert.Equal(typeof(bool?), property.PropertyType);
        Assert.Null(property.GetCustomAttribute<RequiredMemberAttribute>());
    }

    [Fact]
    public void Old_payload_without_new_fields_deserializes()
    {
        var itemId = Guid.NewGuid();
        var updated = JsonSerializer.Deserialize<CatalogItemUpdatedIntegrationEvent>(
            $$"""{"ItemId":"{{itemId}}","Name":"Widget","CategoryId":"{{Guid.NewGuid()}}"}"""
        );
        var deactivated = JsonSerializer.Deserialize<CatalogItemDeactivatedIntegrationEvent>(
            $$"""{"ItemId":"{{itemId}}"}"""
        );

        Assert.Null(updated!.TrackInventory);
        Assert.Null(updated.IsActive);
        Assert.Null(deactivated!.TrackInventory);
    }
}
