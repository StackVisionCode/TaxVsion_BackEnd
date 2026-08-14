using System.Globalization;
using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Calendar.Domain.Types;

/// <summary>
/// Catalogo de tipos de cita de la firma: cuanto dura por defecto, de que color se pinta, si es
/// virtual y si solapar es un error o solo un aviso.
///
/// <para>
/// <see cref="DailyCap"/> es la respuesta a la temporada: un preparador no puede tomar catorce
/// entregas de documentos el 10 de abril aunque tecnicamente le quepan en la agenda.
/// </para>
/// </summary>
public sealed class AppointmentType : AggregateRoot
{
    public const int MaxNameLength = 80;
    public const int MaxDurationHours = 8;
    public const int MinDurationMinutes = 5;

    public string Name { get; private set; } = default!;

    public TimeSpan DefaultDuration { get; private set; }

    public string ColorHex { get; private set; } = default!;

    public bool IsVirtual { get; private set; }

    /// <summary>
    /// Si solapar con otra cita es un error o solo un aviso. Bloquear siempre es paternalista —un
    /// preparador puede querer solapar a proposito—; no avisar nunca es inutil.
    /// </summary>
    public bool BlocksOnConflict { get; private set; }

    /// <summary>Tope de citas de este tipo por persona y dia. Null = sin tope.</summary>
    public int? DailyCap { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    private AppointmentType() { }

    public static Result<AppointmentType> Create(
        Guid tenantId,
        string? name,
        TimeSpan defaultDuration,
        string? colorHex,
        DateTime nowUtc,
        bool isVirtual = false,
        bool blocksOnConflict = false,
        int? dailyCap = null
    )
    {
        var validated = Validate(name, defaultDuration, colorHex, dailyCap);
        if (validated.IsFailure)
            return Result.Failure<AppointmentType>(validated.Error);

        var type = new AppointmentType
        {
            Id = Guid.NewGuid(),
            Name = name!.Trim(),
            DefaultDuration = defaultDuration,
            ColorHex = Normalize(colorHex!),
            IsVirtual = isVirtual,
            BlocksOnConflict = blocksOnConflict,
            DailyCap = dailyCap,
            IsActive = true,
            CreatedAtUtc = nowUtc,
        };
        type.SetTenant(tenantId);

        return Result.Success(type);
    }

    public Result Update(
        string? name,
        TimeSpan defaultDuration,
        string? colorHex,
        bool isVirtual,
        bool blocksOnConflict,
        int? dailyCap
    )
    {
        var validated = Validate(name, defaultDuration, colorHex, dailyCap);
        if (validated.IsFailure)
            return validated;

        Name = name!.Trim();
        DefaultDuration = defaultDuration;
        ColorHex = Normalize(colorHex!);
        IsVirtual = isVirtual;
        BlocksOnConflict = blocksOnConflict;
        DailyCap = dailyCap;

        return Result.Success();
    }

    /// <summary>
    /// Se desactiva, no se borra: las citas pasadas apuntan a su tipo y borrarlo las dejaria sin
    /// explicacion.
    /// </summary>
    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    private static Result Validate(string? name, TimeSpan duration, string? colorHex, int? dailyCap)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(TypeErrors.NameEmpty);

        if (name.Trim().Length > MaxNameLength)
            return Result.Failure(TypeErrors.NameTooLong);

        if (duration < TimeSpan.FromMinutes(MinDurationMinutes) || duration > TimeSpan.FromHours(MaxDurationHours))
            return Result.Failure(TypeErrors.DurationOutOfRange);

        if (!IsHexColor(colorHex))
            return Result.Failure(TypeErrors.ColorInvalid);

        return dailyCap is <= 0 ? Result.Failure(TypeErrors.DailyCapOutOfRange) : Result.Success();
    }

    private static string Normalize(string colorHex) => colorHex.Trim().ToUpperInvariant();

    private static bool IsHexColor(string? value)
    {
        var trimmed = value?.Trim();
        if (trimmed is null || trimmed.Length != 7 || trimmed[0] != '#')
            return false;

        for (var i = 1; i < trimmed.Length; i++)
        {
            if (!Uri.IsHexDigit(trimmed[i]))
                return false;
        }

        return true;
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Name} ({DefaultDuration})");
}
