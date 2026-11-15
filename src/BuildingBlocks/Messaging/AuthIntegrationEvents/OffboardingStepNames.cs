namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Marca un consumer de <see cref="UserOffboardedIntegrationEvent"/> que solo delega el trabajo (p. ej. reenvía un
/// comando). La política común no acusa por él: el acuse lo publica quien termina el trabajo, con
/// <see cref="OffboardingStepNames.CompletedBy"/>, y así Auth no da el paso por hecho antes de tiempo.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AcknowledgesOffboardingLaterAttribute : Attribute;

/// <summary>Cómo se nombra un paso del retiro: servicio (carpeta de src/Services) + clase del consumer.</summary>
public static class OffboardingStepNames
{
    /// <summary>
    /// Servicio dueño de un handler: el segundo tramo del ensamblado (<c>TaxVision.Calendar.Application</c> →
    /// <c>Calendar</c>). Fuera de esa convención, el nombre del ensamblado completo.
    /// </summary>
    public static string ServiceOf(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        var name = handlerType.Assembly.GetName().Name ?? handlerType.Assembly.FullName ?? "unknown";
        var parts = name.Split('.');
        return parts.Length > 1 && parts[0] == "TaxVision" ? parts[1] : name;
    }

    /// <summary>
    /// Acuse del paso de un consumer marcado con <see cref="AcknowledgesOffboardingLaterAttribute"/>, con el mismo
    /// servicio y paso que habría publicado la política. Se publica por el outbox junto con el trabajo terminado.
    /// </summary>
    public static UserOffboardingStepCompletedIntegrationEvent CompletedBy(
        Type deferringConsumer,
        Guid tenantId,
        Guid userId,
        string? correlationId,
        DateTime completedAtUtc
    )
    {
        ArgumentNullException.ThrowIfNull(deferringConsumer);
        if (!deferringConsumer.IsDefined(typeof(AcknowledgesOffboardingLaterAttribute), inherit: false))
            throw new ArgumentException(
                $"{deferringConsumer.Name} is not marked with {nameof(AcknowledgesOffboardingLaterAttribute)}: "
                    + "the common policy already acknowledges it.",
                nameof(deferringConsumer)
            );

        return new UserOffboardingStepCompletedIntegrationEvent
        {
            TenantId = tenantId,
            CorrelationId = correlationId ?? string.Empty,
            UserId = userId,
            Service = ServiceOf(deferringConsumer),
            Step = deferringConsumer.Name,
            CompletedAtUtc = completedAtUtc,
        };
    }
}
