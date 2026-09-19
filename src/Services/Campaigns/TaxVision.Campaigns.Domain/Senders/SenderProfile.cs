using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Domain.Senders;

/// <summary>Estado de un <see cref="SenderProfile"/>: solo un remitente <c>Active</c> se puede seleccionar en un envío.</summary>
public enum SenderProfileStatus
{
    Active = 0,
    Disabled = 1,
}

/// <summary>
/// Aggregate root de un <b>remitente</b> por canal — la arista "quién envía" (<c>Domain_Design.md §6</c>).
/// Guarda una referencia OPACA (<see cref="SenderRef"/>: from/dominio de Email, senderId/número de SMS,
/// número WABA, app de Push) que el EJECUTOR resuelve contra su config/secretos de proveedor.
/// <b>NUNCA</b> guarda secretos de proveedor — esos viven en el ejecutor. SIN dinero.
/// </summary>
public sealed class SenderProfile : TenantEntity
{
    public const int MaxNameLength = 200;
    public const int MaxSenderRefLength = 320;

    private SenderProfile() { }

    /// <summary>Canal ÚNICO al que aplica este remitente (no es un flag combinado).</summary>
    public CampaignChannel Channel { get; private set; }
    public string Name { get; private set; } = default!;

    /// <summary>Referencia opaca al remitente que el ejecutor resuelve (from/senderId/número/app). No es un secreto.</summary>
    public string SenderRef { get; private set; } = default!;
    public SenderProfileStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<SenderProfile> Create(Guid tenantId, CampaignChannel channel, string name, string senderRef)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<SenderProfile>(SenderProfileErrors.TenantRequired);
        if (!IsSingleChannel(channel))
            return Result.Failure<SenderProfile>(SenderProfileErrors.ChannelInvalid);

        var norm = Normalize(name, senderRef);
        if (norm.IsFailure)
            return Result.Failure<SenderProfile>(norm.Error);

        var now = DateTime.UtcNow;
        var sender = new SenderProfile
        {
            Id = Guid.NewGuid(),
            Channel = channel,
            Name = norm.Value.Name,
            SenderRef = norm.Value.SenderRef,
            Status = SenderProfileStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        sender.SetTenant(tenantId);
        return Result.Success(sender);
    }

    public Result Update(string name, string senderRef)
    {
        var norm = Normalize(name, senderRef);
        if (norm.IsFailure)
            return Result.Failure(norm.Error);

        Name = norm.Value.Name;
        SenderRef = norm.Value.SenderRef;
        Touch();
        return Result.Success();
    }

    public void Activate()
    {
        Status = SenderProfileStatus.Active;
        Touch();
    }

    public void Disable()
    {
        Status = SenderProfileStatus.Disabled;
        Touch();
    }

    public bool IsActive => Status == SenderProfileStatus.Active;

    /// <summary>Exactamente un flag de canal (no <c>None</c>, no combinación).</summary>
    private static bool IsSingleChannel(CampaignChannel channel) =>
        channel != CampaignChannel.None && (channel & (channel - 1)) == 0;

    private static Result<(string Name, string SenderRef)> Normalize(string name, string senderRef)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<(string, string)>(SenderProfileErrors.NameRequired);
        if (name.Trim().Length > MaxNameLength)
            return Result.Failure<(string, string)>(SenderProfileErrors.NameTooLong);
        if (string.IsNullOrWhiteSpace(senderRef))
            return Result.Failure<(string, string)>(SenderProfileErrors.SenderRefRequired);
        if (senderRef.Trim().Length > MaxSenderRefLength)
            return Result.Failure<(string, string)>(SenderProfileErrors.SenderRefTooLong);

        return Result.Success((name.Trim(), senderRef.Trim()));
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
