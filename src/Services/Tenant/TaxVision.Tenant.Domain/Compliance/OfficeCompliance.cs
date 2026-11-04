using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Domain.Compliance;

/// <summary>
/// Cumplimiento de la oficina, solo para administradores: EFIN cifrado, responsable de seguridad
/// ("Qualified Individual" de la FTC Safeguards Rule), fechas del WISP, contacto de incidentes y años de
/// retención. Agregado aparte del perfil público, con la misma clave (el tenant): otro permiso, otras
/// reglas y no sube la versión del perfil. Solo salen del servicio los años de retención y el aviso del WISP.
/// </summary>
public sealed class OfficeCompliance : ITenantOwned
{
    private OfficeCompliance() { }

    public Guid TenantId { get; private set; }
    public EncryptedEfin? Efin { get; private set; }

    [UserReference(
        UserReferenceKind.ActiveWork,
        HandledBy = "SecurityCoordinatorOffboardedConsumer",
        Note = "Designación legal: se vacía y se avisa a los admins (D-3), no pasa al sucesor."
    )]
    public Guid? SecurityCoordinatorUserId { get; private set; }
    public DateOnly? WispAdoptedOn { get; private set; }
    public DateOnly? WispLastReviewedOn { get; private set; }
    public string? IncidentContactEmail { get; private set; }
    public string? IncidentContactPhone { get; private set; }
    public int? RetentionYears { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid? UpdatedByUserId { get; private set; }

    // Último recordatorio del WISP avisado: a qué vencimiento correspondía, en qué etapa y qué día (de la oficina).
    public DateOnly? WispReminderDueOn { get; private set; }
    public WispReviewReminderStage? WispReminderStage { get; private set; }
    public DateOnly? WispReminderSentOn { get; private set; }

    /// <summary>Vence la revisión anual: (última revisión ?? adopción) + 12 meses. Sin fechas, no vence.</summary>
    public DateOnly? WispReviewDueOn =>
        (WispLastReviewedOn ?? WispAdoptedOn)?.AddMonths(WispReviewSchedule.ReviewIntervalMonths);

    /// <summary>Cumplimiento vacío. Solo se persiste si un guardado le cambia algo.</summary>
    public static OfficeCompliance CreateEmpty(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Office compliance needs a tenant.", nameof(tenantId));

        return new OfficeCompliance { TenantId = tenantId };
    }

    /// <summary>
    /// Reemplaza los datos de cumplimiento. Valida todo antes de tocar nada; devuelve <c>true</c> si algo
    /// cambió. El EFIN llega ya resuelto (seguir igual, borrar o el nuevo cifrado).
    /// </summary>
    public Result<bool> Update(OfficeComplianceDraft draft, EfinChange efin, Guid actorId, DateTime nowUtc)
    {
        var details = OfficeComplianceDetails.Create(draft, nowUtc);
        if (details.IsFailure)
            return Result.Failure<bool>(details.Error);

        var efinChanged = ApplyEfin(efin);
        var detailsChanged = ApplyDetails(details.Value);
        if (!efinChanged && !detailsChanged)
            return Result.Success(false);

        UpdatedAtUtc = nowUtc;
        UpdatedByUserId = actorId;
        return Result.Success(true);
    }

    /// <summary>
    /// Cada vez que un administrador ve el EFIN completo queda constancia. Sin EFIN no hay nada que revelar.
    /// </summary>
    public Result<EfinRevealRecord> RecordEfinReveal(Guid actorId, DateTime nowUtc) =>
        Efin is null
            ? Result.Failure<EfinRevealRecord>(OfficeComplianceErrors.EfinNotSet)
            : Result.Success(EfinRevealRecord.Create(TenantId, actorId, nowUtc));

    /// <summary>
    /// El responsable de seguridad fue retirado de la oficina: la designación queda vacante. No pasa al
    /// sucesor (es una designación legal que hace la oficina). Solo actúa si el responsable sigue siendo
    /// <paramref name="userId"/>; devuelve <c>true</c> si cambió. <paramref name="actorId"/> null = sistema.
    /// </summary>
    public bool VacateSecurityCoordinatorOnOffboarding(Guid userId, Guid? actorId, DateTime nowUtc)
    {
        if (userId == Guid.Empty || SecurityCoordinatorUserId != userId)
            return false;

        SecurityCoordinatorUserId = null;
        UpdatedAtUtc = nowUtc;
        UpdatedByUserId = actorId;
        return true;
    }

    /// <summary>
    /// Decide si hoy (calendario de la oficina) toca avisar la revisión del WISP y, si toca, lo deja anotado.
    /// DueSoon una vez a ≤ 30 días; Overdue al vencer y otra vez cada 30 días. Otro vencimiento (cambiaron las
    /// fechas, p. ej. "Mark reviewed today") reinicia el ciclo. Idempotente: el mismo día no avisa dos veces.
    /// </summary>
    public WispReviewReminder? RegisterWispReviewReminder(DateOnly officeToday)
    {
        if (WispReviewDueOn is not { } dueOn)
            return null;

        var stage = StageOn(dueOn, officeToday);
        if (stage is null || !ShouldNotify(dueOn, stage.Value, officeToday))
            return null;

        WispReminderDueOn = dueOn;
        WispReminderStage = stage;
        WispReminderSentOn = officeToday;
        return new WispReviewReminder(dueOn, stage.Value);
    }

    // La clave es el tenant: no se reasigna después de crear el registro.
    void ITenantOwned.SetTenant(Guid tenantId)
    {
        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new InvalidOperationException("Office compliance cannot move to another tenant.");
        TenantId = tenantId;
    }

    private static WispReviewReminderStage? StageOn(DateOnly dueOn, DateOnly today)
    {
        if (today >= dueOn)
            return WispReviewReminderStage.Overdue;
        return today >= dueOn.AddDays(-WispReviewSchedule.DueSoonDays) ? WispReviewReminderStage.DueSoon : null;
    }

    private bool ShouldNotify(DateOnly dueOn, WispReviewReminderStage stage, DateOnly today)
    {
        // Nada avisado para este vencimiento: ciclo nuevo.
        if (
            WispReminderDueOn != dueOn
            || WispReminderStage is not { } lastStage
            || WispReminderSentOn is not { } sentOn
        )
            return true;

        if (stage != lastStage)
            return stage == WispReviewReminderStage.Overdue;

        return stage == WispReviewReminderStage.Overdue
            && today >= sentOn.AddDays(WispReviewSchedule.OverdueRepeatDays);
    }

    private bool ApplyEfin(EfinChange change)
    {
        switch (change.Kind)
        {
            case EfinChangeKind.Clear when Efin is not null:
                Efin = null;
                return true;
            case EfinChangeKind.Replace:
                Efin = change.NewValue;
                return true;
            default:
                return false;
        }
    }

    private bool ApplyDetails(OfficeComplianceDetails details)
    {
        if (
            SecurityCoordinatorUserId == details.SecurityCoordinatorUserId
            && WispAdoptedOn == details.WispAdoptedOn
            && WispLastReviewedOn == details.WispLastReviewedOn
            && IncidentContactEmail == details.IncidentContactEmail
            && IncidentContactPhone == details.IncidentContactPhone
            && RetentionYears == details.RetentionYears
        )
            return false;

        SecurityCoordinatorUserId = details.SecurityCoordinatorUserId;
        WispAdoptedOn = details.WispAdoptedOn;
        WispLastReviewedOn = details.WispLastReviewedOn;
        IncidentContactEmail = details.IncidentContactEmail;
        IncidentContactPhone = details.IncidentContactPhone;
        RetentionYears = details.RetentionYears;
        return true;
    }
}
