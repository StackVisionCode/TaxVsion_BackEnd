using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Domain.Runs;

/// <summary>
/// Unidad destinatario/canal dentro de un <see cref="CampaignRun"/> — entidad hija, nunca se
/// crea/muta desde fuera del aggregate (miembros mutables <c>internal</c>). Slice 2: un solo
/// intento (<c>AttemptNo = 1</c>); la entidad <c>CampaignDispatchAttempt</c> con reintentos es fase
/// posterior. Id generado en dominio → EF <c>ValueGeneratedNever()</c> (guardrail 10).
/// </summary>
public sealed class CampaignRecipient
{
    public Guid Id { get; }
    public Guid RunId { get; }
    public Guid TenantId { get; }
    public string ContactRef { get; } = default!;
    public CampaignChannel Channel { get; }
    public string? Email { get; }
    public string? PhoneE164 { get; }
    public string DispatchId { get; } = default!;
    public int AttemptNo { get; }
    public DispatchState State { get; private set; }
    public string? Reason { get; private set; }
    public string? ProviderRef { get; private set; }
    public DateTime? AcceptedAtUtc { get; private set; }
    public DateTime? DeliveredAtUtc { get; private set; }
    public DateTime? SettledAtUtc { get; private set; }

    private CampaignRecipient() { }

    private CampaignRecipient(
        Guid id,
        Guid runId,
        Guid tenantId,
        string contactRef,
        CampaignChannel channel,
        string? email,
        string? phoneE164
    )
    {
        Id = id;
        RunId = runId;
        TenantId = tenantId;
        ContactRef = contactRef;
        Channel = channel;
        Email = email;
        PhoneE164 = phoneE164;
        AttemptNo = 1;
        DispatchId = $"{id:N}-{AttemptNo}";
    }

    internal static CampaignRecipient Materialize(
        Guid runId,
        Guid tenantId,
        string contactRef,
        CampaignChannel channel,
        string? email,
        string? phoneE164
    )
    {
        var recipient = new CampaignRecipient(Guid.NewGuid(), runId, tenantId, contactRef, channel, email, phoneE164);
        if (recipient.HasDestination())
        {
            recipient.State = DispatchState.Pending;
        }
        else
        {
            recipient.State = DispatchState.Skipped;
            recipient.Reason = "no_destination";
            recipient.SettledAtUtc = DateTime.UtcNow;
        }
        return recipient;
    }

    /// <summary>Pending → Dispatched al emitir el evento de dispatch. No-op si no está Pending.</summary>
    internal void MarkDispatched()
    {
        if (State != DispatchState.Pending)
            return;
        State = DispatchState.Dispatched;
    }

    /// <summary>Aplica el resultado del ejecutor con guard idempotente (transición monótona).</summary>
    internal bool ApplyOutcome(DispatchOutcome outcome, string? providerRef, string? reason)
    {
        // Ya terminal (o settled) — solo permitir reconciliación de Accepted/Unknown a Delivered/Failed.
        var now = DateTime.UtcNow;
        switch (outcome)
        {
            case DispatchOutcome.Accepted:
                if (State is DispatchState.Delivered or DispatchState.Failed or DispatchState.Skipped)
                    return false;
                State = DispatchState.Accepted;
                AcceptedAtUtc ??= now;
                SettledAtUtc ??= now;
                break;
            case DispatchOutcome.Delivered:
                if (State == DispatchState.Delivered)
                    return false;
                State = DispatchState.Delivered;
                DeliveredAtUtc ??= now;
                SettledAtUtc ??= now;
                break;
            case DispatchOutcome.Failed:
                if (State is DispatchState.Delivered or DispatchState.Failed)
                    return false;
                State = DispatchState.Failed;
                SettledAtUtc ??= now;
                break;
            case DispatchOutcome.Skipped:
                if (State is DispatchState.Delivered or DispatchState.Failed or DispatchState.Skipped)
                    return false;
                State = DispatchState.Skipped;
                SettledAtUtc ??= now;
                break;
            case DispatchOutcome.Unknown:
                if (State is DispatchState.Delivered or DispatchState.Failed)
                    return false;
                State = DispatchState.Unknown;
                SettledAtUtc ??= now;
                break;
            default:
                return false;
        }

        ProviderRef = providerRef ?? ProviderRef;
        Reason = reason ?? Reason;
        return true;
    }

    /// <summary>Una unidad es "settled" (cuenta para el cierre) si no está Pending ni en vuelo.</summary>
    internal bool IsSettled =>
        State
            is DispatchState.Accepted
                or DispatchState.Delivered
                or DispatchState.Failed
                or DispatchState.Skipped
                or DispatchState.Unknown;

    private bool HasDestination() =>
        Channel switch
        {
            CampaignChannel.Email => !string.IsNullOrWhiteSpace(Email),
            CampaignChannel.Sms or CampaignChannel.WhatsApp => !string.IsNullOrWhiteSpace(PhoneE164),
            _ => true,
        };
}
