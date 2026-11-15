using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Campaigns.Domain.Settings;

/// <summary>
/// P32: la ventana en que la oficina permite enviar los SMS de sus campañas, en su hora local. Solo puede acortar la
/// permitida por la ley de EE. UU. para marketing (TCPA: de 8:00 a 21:00); nunca salir de ella.
/// </summary>
public sealed class CampaignSmsWindow : TenantEntity
{
    public static readonly TimeOnly LegalOpens = new(8, 0);
    public static readonly TimeOnly LegalCloses = new(21, 0);

    private CampaignSmsWindow() { }

    public TimeOnly Opens { get; private set; }
    public TimeOnly Closes { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance, Note = "Quién la cambió por última vez.")]
    public Guid UpdatedByUserId { get; private set; }

    public static Result<CampaignSmsWindow> Set(
        Guid tenantId,
        TimeOnly opens,
        TimeOnly closes,
        Guid byUserId,
        DateTime nowUtc
    )
    {
        var window = new CampaignSmsWindow { Id = Guid.NewGuid() };
        window.SetTenant(tenantId);
        var changed = window.Change(opens, closes, byUserId, nowUtc);
        return changed.IsSuccess ? Result.Success(window) : Result.Failure<CampaignSmsWindow>(changed.Error);
    }

    public Result Change(TimeOnly opens, TimeOnly closes, Guid byUserId, DateTime nowUtc)
    {
        if (opens < LegalOpens || closes > LegalCloses)
            return Result.Failure(CampaignSmsWindowErrors.OutsideLegalHours);
        if (opens >= closes)
            return Result.Failure(CampaignSmsWindowErrors.OpensAfterCloses);

        Opens = opens;
        Closes = closes;
        UpdatedByUserId = byUserId;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}

public static class CampaignSmsWindowErrors
{
    public static readonly Error OutsideLegalHours = new(
        "Campaigns.SmsWindow.OutsideLegalHours",
        "SMS sending hours must be between 8:00 AM and 9:00 PM."
    );

    public static readonly Error OpensAfterCloses = new(
        "Campaigns.SmsWindow.OpensAfterCloses",
        "The start time must be earlier than the end time."
    );
}
