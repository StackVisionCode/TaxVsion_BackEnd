using BuildingBlocks.Messaging.AuthIntegrationEvents;

namespace TaxVision.Tasks.ClientForms.Application.Offboarding;

internal static class OffboardingSupport
{
    public static string CorrelationIdOf(UserOffboardedIntegrationEvent msg) =>
        string.IsNullOrWhiteSpace(msg.CorrelationId) ? msg.EventId.ToString("N") : msg.CorrelationId;

    // Un sucesor igual al retirado no cuenta como sucesor.
    public static Guid? SuccessorOf(UserOffboardedIntegrationEvent msg) =>
        msg.SuccessorUserId is { } candidate && candidate != msg.UserId && candidate != Guid.Empty ? candidate : null;

    // Quien ejecutó el retiro, salvo que sea el propio retirado (o falte).
    public static Guid? OffboardedByOf(UserOffboardedIntegrationEvent msg) =>
        msg.OffboardedByUserId is { } actor && actor != msg.UserId && actor != Guid.Empty ? actor : null;
}
