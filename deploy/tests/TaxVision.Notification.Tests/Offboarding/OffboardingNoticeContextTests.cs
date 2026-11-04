using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Tests.Offboarding;

/// <summary>Id de dedupe de correo y push de los avisos del retiro.</summary>
public sealed class OffboardingNoticeContextTests
{
    [Fact]
    public void Sin_DeliveryKey_dedupa_por_evento()
    {
        var context = Context(Guid.NewGuid(), NotificationKinds.TenantSecurityCoordinatorVacated, deliveryKey: null);

        Assert.Equal(context.EventId, context.DeliveryId);
    }

    [Fact]
    public void Con_DeliveryKey_es_estable_entre_eventos_y_distinto_por_aviso()
    {
        var first = Context(Guid.NewGuid(), NotificationKinds.CloudStorageConnectionReviewRequired, "connection:a:b");
        var again = Context(Guid.NewGuid(), NotificationKinds.CloudStorageConnectionReviewRequired, "connection:a:b");
        var otherKind = Context(
            Guid.NewGuid(),
            NotificationKinds.ConnectorsOfficeMailboxReviewRequired,
            "connection:a:b"
        );

        Assert.Equal(first.DeliveryId, again.DeliveryId);
        Assert.NotEqual(first.EventId, first.DeliveryId);
        Assert.NotEqual(first.DeliveryId, otherKind.DeliveryId);
    }

    private static OffboardingNoticeContext Context(Guid eventId, string kind, string? deliveryKey) =>
        new(
            Guid.Parse("8a0d8f8e-3f7e-4c55-9d3a-5b2f1c0e7a11"),
            eventId,
            DateTime.UtcNow,
            "source.v1",
            kind,
            NotificationCategory.Collaboration,
            "email.v1",
            "https://office.test",
            null,
            "group",
            DeliveryKey: deliveryKey
        );
}
