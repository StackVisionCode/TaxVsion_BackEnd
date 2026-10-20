using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Tests;

/// <summary>Stub de IInAppSourceGate que siempre permite publicar (defaults de test).</summary>
internal sealed class AlwaysOnInAppSource : IInAppSourceGate
{
    public static readonly AlwaysOnInAppSource Instance = new();

    public bool PublishesInApp(InAppSourceGroup group) => true;
}
