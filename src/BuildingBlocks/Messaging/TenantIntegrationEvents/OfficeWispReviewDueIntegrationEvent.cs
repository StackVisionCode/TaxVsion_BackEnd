namespace BuildingBlocks.Messaging.TenantIntegrationEvents;

/// <summary>
/// La revisión anual del plan escrito de seguridad (WISP, FTC Safeguards Rule) de una oficina está por
/// vencer o venció. Lo publica el job diario de Tenant, una vez por etapa; Notification avisa al
/// responsable de seguridad y a quienes administran la oficina.
/// </summary>
public sealed record OfficeWispReviewDueIntegrationEvent : IntegrationEvent
{
    public required Guid OfficeTenantId { get; init; }

    /// <summary>Fecha (en el calendario de la oficina) en que vence la revisión.</summary>
    public required DateOnly DueOn { get; init; }

    /// <summary><see cref="OfficeWispReviewStages"/>.</summary>
    public required string Stage { get; init; }

    /// <summary>Responsable de seguridad, si la oficina lo designó.</summary>
    public Guid? SecurityCoordinatorUserId { get; init; }
}

public static class OfficeWispReviewStages
{
    /// <summary>Faltan 30 días o menos.</summary>
    public const string DueSoon = "DueSoon";

    /// <summary>Ya venció (se repite cada 30 días mientras siga vencida).</summary>
    public const string Overdue = "Overdue";
}
