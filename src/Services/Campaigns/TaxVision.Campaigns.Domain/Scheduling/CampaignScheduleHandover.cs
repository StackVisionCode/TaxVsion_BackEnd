using BuildingBlocks.Domain;

namespace TaxVision.Campaigns.Domain.Scheduling;

/// <summary>
/// Constancia (solo se agrega) de lo que se hizo con un agendado al retirarse quien lo disparaba (a quién
/// pasó y con qué visibilidad, o que quedó pausado) o al reanudarlo un admin en su propio nombre; quién lo
/// hizo y qué evento lo originó.
/// Guarda la cadena de responsables sin tocar la procedencia del agendado.
/// </summary>
public sealed class CampaignScheduleHandover : TenantEntity
{
    private CampaignScheduleHandover() { }

    public Guid ScheduleId { get; private set; }
    public Guid CampaignId { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid FromUserId { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid? ToUserId { get; private set; }
    public bool? ToCanViewAllCustomers { get; private set; }
    public CampaignScheduleHandoverOutcome Outcome { get; private set; }

    /// <summary>Quién ejecutó el retiro o la reanudación; null = sistema.</summary>
    [UserReference(UserReferenceKind.Provenance)]
    public Guid? PerformedByUserId { get; private set; }

    /// <summary>Evento de retiro que lo originó (trazabilidad). Null = acción manual (reanudación).</summary>
    public Guid? SourceEventId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>El agendado pasó al sucesor; se toma del agendado ya traspasado.</summary>
    public static CampaignScheduleHandover Reassigned(
        CampaignSchedule schedule,
        Guid fromUserId,
        Guid? performedByUserId,
        Guid sourceEventId,
        DateTime nowUtc
    ) =>
        New(
            schedule,
            fromUserId,
            CampaignScheduleHandoverOutcome.Reassigned,
            schedule.RunAsUserId,
            schedule.CreatorCanViewAllCustomers,
            performedByUserId,
            sourceEventId,
            nowUtc
        );

    /// <summary>El agendado quedó pausado por falta de sucesor elegible.</summary>
    public static CampaignScheduleHandover Paused(
        CampaignSchedule schedule,
        Guid fromUserId,
        Guid? performedByUserId,
        Guid sourceEventId,
        DateTime nowUtc
    ) =>
        New(
            schedule,
            fromUserId,
            CampaignScheduleHandoverOutcome.Paused,
            null,
            null,
            performedByUserId,
            sourceEventId,
            nowUtc
        );

    /// <summary>Un admin lo reanudó en su nombre; se toma del agendado ya reanudado.</summary>
    public static CampaignScheduleHandover ResumedByAdmin(
        CampaignSchedule schedule,
        Guid fromUserId,
        DateTime nowUtc
    ) =>
        New(
            schedule,
            fromUserId,
            CampaignScheduleHandoverOutcome.ResumedByAdmin,
            schedule.RunAsUserId,
            schedule.CreatorCanViewAllCustomers,
            schedule.RunAsUserId,
            null,
            nowUtc
        );

    private static CampaignScheduleHandover New(
        CampaignSchedule schedule,
        Guid fromUserId,
        CampaignScheduleHandoverOutcome outcome,
        Guid? toUserId,
        bool? toCanViewAllCustomers,
        Guid? performedByUserId,
        Guid? sourceEventId,
        DateTime nowUtc
    )
    {
        var handover = new CampaignScheduleHandover
        {
            Id = Guid.NewGuid(),
            ScheduleId = schedule.Id,
            CampaignId = schedule.CampaignId,
            FromUserId = fromUserId,
            ToUserId = toUserId,
            ToCanViewAllCustomers = toCanViewAllCustomers,
            Outcome = outcome,
            PerformedByUserId = performedByUserId,
            SourceEventId = sourceEventId,
            OccurredAtUtc = nowUtc,
        };
        handover.SetTenant(schedule.TenantId);
        return handover;
    }
}
