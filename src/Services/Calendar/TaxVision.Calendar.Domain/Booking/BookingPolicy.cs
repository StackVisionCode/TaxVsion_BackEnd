using System.Text.Json;
using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Calendar.Domain.Booking;

public static class BookingPolicyErrors
{
    public static readonly Error OutOfRange = new(
        "Calendar.BookingPolicy.OutOfRange",
        "Notice up to 14 days, booking window 1 to 365 days, buffer up to 240 minutes and a slot step of 5, 10, 15, 20, 30 or 60 minutes."
    );

    public static readonly Error NoBookableTypes = new(
        "Calendar.BookingPolicy.NoBookableTypes",
        "Choose at least one appointment type clients can book."
    );

    public static readonly Error TypeNotBookable = new(
        "Calendar.BookingPolicy.TypeNotBookable",
        "Only active appointment types can be offered to clients."
    );
}

/// <summary>
/// Cómo reservan los clientes desde el portal (D-CAL-C/E/F), una por oficina. Apagada por defecto:
/// la oficina decide qué tipos ofrece y con cuánta anticipación.
/// </summary>
public sealed class BookingPolicy : TenantEntity
{
    public const int DefaultMinNoticeMinutes = 1440;
    public const int DefaultMaxDaysAhead = 60;
    public const int DefaultSlotStepMinutes = 15;
    public const int DefaultCustomerChangeCutoffMinutes = 1440;
    public static readonly IReadOnlyList<int> AllowedSlotSteps = [5, 10, 15, 20, 30, 60];

    private static bool IsAllowedStep(int minutes)
    {
        foreach (var step in AllowedSlotSteps)
        {
            if (step == minutes)
                return true;
        }
        return false;
    }

    private const int MaxNoticeMinutes = 14 * 1440;
    private const int MaxBufferMinutes = 240;
    private const int MaxBookableTypes = 50;

    private BookingPolicy() { }

    public bool Enabled { get; private set; }
    public string BookableTypeIdsJson { get; private set; } = "[]";
    public int MinNoticeMinutes { get; private set; } = DefaultMinNoticeMinutes;
    public int MaxDaysAhead { get; private set; } = DefaultMaxDaysAhead;
    public int BufferMinutes { get; private set; }
    public int SlotStepMinutes { get; private set; } = DefaultSlotStepMinutes;
    public int CustomerChangeCutoffMinutes { get; private set; } = DefaultCustomerChangeCutoffMinutes;

    /// <summary>La oficina aprueba cada reserva del portal (por defecto): queda pedida hasta que alguien la acepte.</summary>
    public bool RequiresApproval { get; private set; } = true;
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<Guid> BookableTypeIds => JsonSerializer.Deserialize<List<Guid>>(BookableTypeIdsJson) ?? [];

    /// <summary>La política de una oficina que nunca la guardó.</summary>
    public static BookingPolicy Default(Guid tenantId)
    {
        var policy = new BookingPolicy { Id = Guid.NewGuid() };
        policy.SetTenant(tenantId);
        return policy;
    }

    /// <summary>Reemplaza la política entera. Los tipos llegan ya verificados como activos.</summary>
    public Result Update(
        bool enabled,
        IReadOnlyList<Guid> bookableTypeIds,
        int minNoticeMinutes,
        int maxDaysAhead,
        int bufferMinutes,
        int slotStepMinutes,
        int customerChangeCutoffMinutes,
        DateTime nowUtc,
        bool requiresApproval = true
    )
    {
        if (
            minNoticeMinutes is < 0 or > MaxNoticeMinutes
            || maxDaysAhead is < 1 or > 365
            || bufferMinutes is < 0 or > MaxBufferMinutes
            || !IsAllowedStep(slotStepMinutes)
            || customerChangeCutoffMinutes is < 0 or > MaxNoticeMinutes
            || bookableTypeIds.Count > MaxBookableTypes
        )
            return Result.Failure(BookingPolicyErrors.OutOfRange);

        // Sin repetidos y en el orden que eligió la oficina.
        var seen = new HashSet<Guid>();
        var types = new List<Guid>();
        foreach (var id in bookableTypeIds)
        {
            if (seen.Add(id))
                types.Add(id);
        }
        if (enabled && types.Count == 0)
            return Result.Failure(BookingPolicyErrors.NoBookableTypes);

        Enabled = enabled;
        BookableTypeIdsJson = JsonSerializer.Serialize(types);
        MinNoticeMinutes = minNoticeMinutes;
        MaxDaysAhead = maxDaysAhead;
        BufferMinutes = bufferMinutes;
        SlotStepMinutes = slotStepMinutes;
        CustomerChangeCutoffMinutes = customerChangeCutoffMinutes;
        RequiresApproval = requiresApproval;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}
