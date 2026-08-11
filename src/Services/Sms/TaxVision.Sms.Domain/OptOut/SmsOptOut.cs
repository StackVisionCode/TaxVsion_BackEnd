using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Sms.Domain.ValueObjects;

namespace TaxVision.Sms.Domain.OptOut;

/// <summary>
/// Fuente única de consentimiento por `(tenant, customer, número)`. STOP → <see cref="SmsOptOutStatus.OptedOut"/>
/// (corta todo envío); START → Subscribed. Idempotente (procesado por webhook inbound que puede reintentar).
/// Tenant-owned para consultas del tenant; el webhook inbound resuelve por `(tenant, phone)` con IgnoreQueryFilters.
/// </summary>
public sealed class SmsOptOut : TenantEntity
{
    public Guid CustomerId { get; private set; }
    public string PhoneE164 { get; private set; } = default!;
    public SmsOptOutStatus Status { get; private set; }
    public string? LastKeyword { get; private set; }
    public DateTime? OptedOutAtUtc { get; private set; }
    public DateTime? OptedInAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private SmsOptOut() { }

    public static SmsOptOut CreateSubscribed(Guid tenantId, Guid customerId, PhoneE164 phone, DateTime nowUtc)
    {
        var optOut = new SmsOptOut
        {
            CustomerId = customerId,
            PhoneE164 = phone.Value,
            Status = SmsOptOutStatus.Subscribed,
            UpdatedAtUtc = nowUtc,
        };
        optOut.SetTenant(tenantId);
        return optOut;
    }

    public Result OptOut(string keyword, DateTime nowUtc)
    {
        Status = SmsOptOutStatus.OptedOut;
        LastKeyword = keyword;
        OptedOutAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result OptIn(string keyword, DateTime nowUtc)
    {
        Status = SmsOptOutStatus.Subscribed;
        LastKeyword = keyword;
        OptedInAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public bool IsOptedOut => Status == SmsOptOutStatus.OptedOut;
}
